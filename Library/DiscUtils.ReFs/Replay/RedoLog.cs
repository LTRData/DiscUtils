using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.IO;
using DiscUtils.ReFs.Replay.Enums;

namespace DiscUtils.ReFs.Replay;

/// <summary>
///     Reader of the redo log written by the "MLog" log library. Layout (verified on clean and dirty images, 4 KiB and 64 KiB clusters; all page
///     checksums matched): two control pages (copies A and B) and a circular data region of 4 KiB pages. An LSN is {u32 page index in the region, u32
///     lap}. Page header: +0 'MLog', +4 volume signature, +8 version 1, +0xC page size 0x1000, +0x10 log GUID, +0x20 open generation, +0x28 entry LSN,
///     +0x30 previous entry LSN, +0x38 pages allocated, +0x3C pages used, +0x54 offset of the entry header (0x78 on every image; Windows accepts
///     any offset from 0x78 to pages allocated x page size). Entry header (offsets from it): +0 LSN, +8 checksum, previous LSN, +0x20 payload
///     size, +0x28 payload offset from the header, +0x30 type (2 data, 1 control). Two header lengths are known. 0x38 (payload at header + 0x38):
///     the type is a u64. 0x40 (payload at header + 0x40; seen on Windows Server 2025 lab images): the type is a u32 followed by a u32 that is 1,
///     then eight bytes that are not cleared. Any other length is rejected for data. The checksum is over all used pages with the checksum qword
///     zeroed: for each page, x = ROL32(prev, 1) xor all 512 qwords, prev = lo32(x) xor hi32(x).
///     <para>
///         Reading follows Windows' scan: the next entry starts after the pages allocated, not the pages used, and an entry whose type is not data is
///         skipped, not taken as the end of the log.
///     </para>
///     <para>
///         Known gap: on storage with more than one data copy (mirror or parity resiliency) Windows repairs a damaged entry from another copy and goes
///         on. This reader sees one copy, so a damaged entry ends the log there. On single-copy storage Windows stops too.
///     </para>
/// </summary>
internal sealed class RedoLog
{
    private const uint Signature = 0x676F4C4D; // 'MLog'
    private const int PageSize = 4096;
    private const int MinimumEntryHeaderOffset = 0x78;
    private const int ChecksumOffset = MinimumEntryHeaderOffset + 8;
    private const int ShortHeaderLength = 0x38;
    private const int LongHeaderLength = 0x40;
    private const uint ControlEntryType = 1;
    private const uint DataEntryType = 2;
    private readonly uint _clusterSize;
    private readonly ControlPage _control;
    private readonly LogLocation _location;

    private readonly IBlockSource _volume;

    private RedoLog(IBlockSource volume, uint clusterSize, LogLocation location, ControlPage control)
    {
        _volume = volume;
        _clusterSize = clusterSize;
        _location = location;
        _control = control;
        PageCount = location.DataClusters * clusterSize / PageSize;
    }

    /// <summary>Number of 4 KiB pages in the circular data region.</summary>
    public ulong PageCount { get; }

    /// <summary>Counter and generation of the control page in use; flags (<see cref="RedoLogFlags.ChecksumsEnabled" />).</summary>
    public (ulong Counter, uint Generation, RedoLogFlags Flags, ulong StartLsn, ulong EndLsn) Control =>
        (_control.Counter, _control.Generation, _control.Flags, _control.StartLsn, _control.EndLsn);

    /// <summary>
    ///     Opens the log: reads both control pages and picks one as Windows does: copy A unless it is invalid, or it
    ///     lacks <see cref="RedoLogFlags.Bit0x2" /> while copy B has it or a higher write counter.
    /// </summary>
    public static RedoLog Open(IBlockSource volume, uint clusterSize, LogLocation location)
    {
        var a = TryReadControl(volume, clusterSize, location, location.ControlA);
        var b = TryReadControl(volume, clusterSize, location, location.ControlB);
        var chosen = (a, b) switch
        {
            (null, null) => null,
            (null, _) => b,
            (_, null) => a,
            _ => !a.Flags.HasFlag(RedoLogFlags.Bit0x2) && (b.Flags.HasFlag(RedoLogFlags.Bit0x2) || a.Counter < b.Counter) ? b : a
        };

        return chosen is null
            ? throw new ReFsFormatException("Neither redo log control page is valid.")
            : new RedoLog(volume, clusterSize, location, chosen);
    }

    /// <summary>
    ///     Reads the data entries from <paramref name="startLsn" /> up to the end of the valid log: an entry is valid when its
    ///     signature, GUID, LSN, previous-LSN chain, generation and checksum all match; the first entry that is not ends the log.
    ///     A valid entry of another type than data is passed over. The next entry starts after the pages the entry has allocated; the
    ///     lap counter advances when the data region wraps.
    /// </summary>
    public IEnumerable<LogEntry> ReadEntries(ulong startLsn)
    {
        var lsn = startLsn;
        ulong? previous = null;
        uint highestGeneration = 0;
        var budget = PageCount + 1;

        while (budget-- > 0)
        {
            var frame = TryReadEntry(lsn, previous, highestGeneration);

            if (frame is null && (uint)lsn != 0)
            {
                lsn = (ulong)((uint)(lsn >> 32) + 1) << 32; // the end of a lap: continue at page 0 of the next lap
                frame = TryReadEntry(lsn, previous, highestGeneration);
            }

            if (frame is null)
            {
                yield break;
            }

            if (frame.Entry is { } entry)
            {
                yield return entry;
            }

            highestGeneration = Math.Max(highestGeneration, frame.Generation);
            previous = lsn;
            var next = (ulong)(uint)lsn + frame.AllocatedPages;
            lsn = next >= PageCount ? (ulong)((uint)(lsn >> 32) + 1) << 32 : (lsn & 0xFFFFFFFF00000000UL) | (uint)next;
        }
    }

    private Frame? TryReadEntry(ulong lsn, ulong? expectedPrevious, uint highestGeneration)
    {
        var page = (uint)lsn;

        if (page >= PageCount)
        {
            return null;
        }

        var first = new byte[PageSize];
        _volume.ReadExactly(PageOffset(page), first);
        ReadOnlySpan<byte> head = first;

        if (head.U32(0) != Signature || head.U32(4) != _control.VolumeSignature || head.U32(8) != 1 || head.U32(0x0C) != PageSize
            || !head.Slice(0x10, 16).SequenceEqual(_control.Guid) || head.U64(0x28) != lsn
            || (expectedPrevious is { } p && head.U64(0x30) != p) || head.U32(0x20) < highestGeneration)
        {
            return null;
        }

        var allocated = head.U32(0x38);
        var used = head.U32(0x3C);
        var headerOffset = head.U32(0x54);

        if (used == 0 || used > allocated || used > int.MaxValue / PageSize || (ulong)page + used > PageCount
            || headerOffset < MinimumEntryHeaderOffset || (ulong)headerOffset + ShortHeaderLength > (ulong)used * PageSize)
        {
            return null;
        }

        var pages = new byte[(int)used * PageSize];
        first.CopyTo(pages, 0);

        if (used > 1)
        {
            _volume.ReadExactly(PageOffset(page + 1), pages.AsSpan(PageSize));
        }

        ReadOnlySpan<byte> all = pages;
        var header = (int)headerOffset;

        if (all.U64(header) != lsn)
        {
            return null;
        }

        if (_control.Flags.HasFlag(RedoLogFlags.ChecksumsEnabled) && ComputeChecksum(pages, header + 8) != all.U64(header + 8))
        {
            return null;
        }

        var generation = head.U32(0x20);

        if (all.U32(header + 0x30) != DataEntryType)
        {
            return new Frame(null, allocated, generation);
        }

        var payloadOffset = PayloadOffset(all, header, DataEntryType);

        if (payloadOffset < 0)
        {
            return null;
        }

        var payloadSize = all.U32(header + 0x20);

        if (payloadSize > pages.Length - payloadOffset)
        {
            return null;
        }

        return new Frame(new LogEntry(pages, payloadOffset, (int)payloadSize), allocated, generation);
    }

    /// <summary>
    ///     Offset in the pages of the payload of an entry of the given type whose header is at <paramref name="header" />, or -1 when the header is not one
    ///     of the two known layouts, has another type or leaves no room for the payload.
    /// </summary>
    private static int PayloadOffset(ReadOnlySpan<byte> pages, int header, uint type)
    {
        var headerLength = pages.U32(header + 0x28);

        if (headerLength is not (ShortHeaderLength or LongHeaderLength) || header + headerLength > pages.Length)
        {
            return -1;
        }

        var known = headerLength switch
        {
            ShortHeaderLength => pages.U64(header + 0x30) == type,
            _ => pages.U32(header + 0x30) == type && pages.U32(header + 0x34) == 1
        };

        return known ? header + (int)headerLength : -1;
    }

    /// <summary>The log checksum over whole pages, treating the u64 at <paramref name="checksumOffset" /> (entry header + 8; 0x80 with the usual header) as zero.</summary>
    internal static ulong ComputeChecksum(ReadOnlySpan<byte> pages, int checksumOffset = ChecksumOffset)
    {
        uint previous = 0;

        for (var offset = 0; offset < pages.Length; offset += PageSize)
        {
            ulong x = (previous << 1) | (previous >> 63);

            for (var i = 0; i < PageSize; i += sizeof(ulong))
            {
                if (offset + i == checksumOffset)
                {
                    continue;
                }

                x ^= pages.U64(offset + i);
            }

            previous = (uint)x ^ (uint)(x >> 32);
        }

        return previous;
    }

    private long PageOffset(uint page)
    {
        var baseOffset = BlockSourceExtensions.ClusterOffset(_location.DataStart, _clusterSize);
        var delta = (long)page * PageSize;

        if (delta > long.MaxValue - baseOffset)
        {
            throw new ReFsFormatException("Redo log page offset overflows.");
        }

        return baseOffset + delta;
    }

    private static ControlPage? TryReadControl(IBlockSource volume, uint clusterSize, LogLocation location, ulong cluster)
    {
        var page = new byte[PageSize];
        volume.ReadExactly(BlockSourceExtensions.ClusterOffset(cluster, clusterSize), page);
        ReadOnlySpan<byte> p = page;
        var header = p.U32(0x54);

        if (p.U32(0) != Signature || p.U32(8) != 1 || p.U32(0x0C) != PageSize || header < MinimumEntryHeaderOffset || header + LongHeaderLength + 0x40 > PageSize)
        {
            return null;
        }

        var payloadOffset = PayloadOffset(p, (int)header, ControlEntryType);

        if (payloadOffset < 0 || payloadOffset + 0x40 > PageSize)
        {
            return null;
        }

        var control = new ControlPage(p, payloadOffset);

        if (control.Flags.HasFlag(RedoLogFlags.ChecksumsEnabled) && ComputeChecksum(page, (int)header + 8) != p.U64((int)header + 8))
        {
            return null;
        }

        var startPages = location.DataStart * clusterSize / PageSize;
        var endPages = (location.DataStart + location.DataClusters) * clusterSize / PageSize;
        return control.RegionStart == startPages && control.RegionEnd == endPages ? control : null;
    }

    /// <summary>A valid entry in the log: its data entry, or null when it is of another type and is passed over.</summary>
    private sealed record Frame(LogEntry? Entry, uint AllocatedPages, uint Generation);

    /// <summary>
    ///     A control page: the page header's volume signature (+4) and log GUID (+0x10), and the control payload: +0 u64 write counter, +8 u64 first
    ///     page of the data region, +0x10 u64 page after it (both counted from the volume start), +0x18 u64 start LSN, +0x20 u64 end LSN,
    ///     +0x38 u32 generation, +0x3C u32 flags.
    /// </summary>
    private sealed class ControlPage
    {
        public ControlPage(ReadOnlySpan<byte> page, int payloadOffset)
        {
            var control = page[payloadOffset..];
            VolumeSignature = page.U32(4);
            Guid = page.Slice(0x10, 16).ToArray();
            Counter = control.U64(0);
            RegionStart = control.U64(8);
            RegionEnd = control.U64(0x10);
            StartLsn = control.U64(0x18);
            EndLsn = control.U64(0x20);
            Generation = control.U32(0x38);
            Flags = (RedoLogFlags)control.U32(0x3C);
        }

        public uint VolumeSignature { get; }

        public byte[] Guid { get; }

        public ulong Counter { get; }

        public ulong RegionStart { get; }

        public ulong RegionEnd { get; }

        public ulong StartLsn { get; }

        public ulong EndLsn { get; }

        public uint Generation { get; }

        public RedoLogFlags Flags { get; }
    }
}