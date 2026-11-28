//
// Copyright (c) 2008-2011, Kenneth Bell
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiscUtils.Internal;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Registry;

/// <summary>
/// A registry hive.
/// </summary>
public class RegistryHive : IDisposable
{
    internal const int BinStart = 4 * Sizes.OneKiB;
    private readonly List<BinHeader> _bins;

    /// <summary>Loaded bins by <see cref="BinHeader.FileOffset"/>; see <see cref="LoadBin"/>.</summary>
    private readonly Dictionary<int, Bin> _binCache = [];

    internal Stream FileStream { get; private set; }

    private readonly HiveHeader _header;
    private readonly Ownership _ownsStream;

    /// <summary>
    /// Initializes a new instance of the RegistryHive class.
    /// </summary>
    /// <param name="filePath">Path to registry hive file. This method will also open
    /// LOG1 and LOG2 files at the same location if there are pending changes to apply
    /// to the registry hive.</param>
    /// <param name="access">Specifies read-only or read/write access</param>
    /// <remarks>
    /// The created object does not assume ownership of the stream.
    /// </remarks>
    public RegistryHive(string filePath, FileAccess access)
        : this(new FileStream(filePath, FileMode.Open, access, access.HasFlag(FileAccess.Write) ? FileShare.None : FileShare.Read),
              Ownership.Dispose,
              OpenLogFiles(filePath, access).ToArray())
    {
    }

    private static IEnumerable<Stream> OpenLogFiles(string hivePath, FileAccess access)
    {
        var log = $"{hivePath}.LOG";

        var log1 = $"{log}1";
        if (File.Exists(log1))
        {
            yield return new FileStream(log1, FileMode.Open, access, access.HasFlag(FileAccess.Write) ? FileShare.None : FileShare.Read);

            var log2 = $"{log}2";
            if (File.Exists(log2))
            {
                yield return new FileStream(log2, FileMode.Open, access, access.HasFlag(FileAccess.Write) ? FileShare.None : FileShare.Read);
            }
        }
        else if (File.Exists(log))
        {
            yield return new FileStream(log, FileMode.Open, access, access.HasFlag(FileAccess.Write) ? FileShare.None : FileShare.Read);
        }
    }

    /// <summary>
    /// Initializes a new instance of the RegistryHive class.
    /// </summary>
    /// <param name="file">Path to registry hive file. This method will also open
    /// LOG1 and LOG2 files at the same location if there are pending changes to apply
    /// to the registry hive.</param>
    /// <param name="access">Specifies read-only or read/write access</param>
    /// <remarks>
    /// The created object does not assume ownership of the stream.
    /// </remarks>
    public RegistryHive(DiscFileInfo file, FileAccess access)
        : this(file.Open(FileMode.Open, access), Ownership.Dispose, OpenLogFiles(file, access).ToArray())
    {
    }

    private static IEnumerable<Stream> OpenLogFiles(DiscFileInfo hivePath, FileAccess access)
    {
        var log = hivePath.FileSystem.GetFileInfo($"{hivePath.FullName}.LOG");

        var log1 = log.FileSystem.GetFileInfo($"{log.FullName}1");
        if (log1.Exists)
        {
            yield return log1.Open(FileMode.Open, access);

            var log2 = log.FileSystem.GetFileInfo($"{log.FullName}2");
            if (log2.Exists)
            {
                yield return log2.Open(FileMode.Open, access);
            }
        }
        else if (log.Exists)
        {
            yield return log.Open(FileMode.Open, access);
        }
    }

    /// <summary>
    /// Initializes a new instance of the RegistryHive class.
    /// </summary>
    /// <param name="hive">The stream containing the registry hive.</param>
    /// <param name="logfiles">LOG1 and LOG2 streams to replay pending changes from</param>
    /// <remarks>
    /// The created object does not assume ownership of the streams.
    /// </remarks>
    public RegistryHive(Stream hive, params Stream[] logfiles)
        : this(hive, Ownership.None, logfiles) { }

    /// <summary>
    /// Initializes a new instance of the RegistryHive class.
    /// </summary>
    /// <param name="hive">The stream containing the registry hive.</param>
    /// <param name="ownership">Whether the new object assumes object of the stream.</param>
    /// <param name="logstreams">LOG1 and LOG2 streams to replay pending changes from</param>
    public RegistryHive(Stream hive, Ownership ownership, params Stream[] logstreams)
    {
        FileStream = hive;
        FileStream.Position = 0;
        _ownsStream = ownership;

        Span<byte> buffer = stackalloc byte[HiveHeader.HeaderSize];
        var validBufferSize = FileStream.Read(buffer);
        buffer[validBufferSize..].Clear();

        _header = new();
        var headerSize = _header.ReadFrom(buffer, throwOnInvalidData: false);

        // If header validation failed or dirty state, look for transaction logs
        if (headerSize == 0 || _header.Sequence1 != _header.Sequence2)
        {
            var logs = logstreams?
                .Where(static log => log.Length > 0x1000)
                .ToArray();

            if (logs is { Length: > 0 })
            {
                // If we are opening a hive read-only, copy to an in-memory buffer
                // to be able to replay logs
                if (!FileStream.CanWrite)
                {
                    var mem = new MemoryStream((int)FileStream.Length);
                    FileStream.Position = 0;
                    FileStream.CopyTo(mem);
                    mem.Position = 0;

                    if (ownership == Ownership.Dispose)
                    {
                        FileStream.Dispose();
                    }

                    FileStream = mem;
                }

                // Open log files
                var logfiles = new LogFile[Math.Min(2, logs.Length)];

                logfiles[0] = new(logs[0]);

                if (logs.Length > 1)
                {
                    logfiles[1] = new(logs[1]);
                }

                // Sort log files in order of sequence
                if (logfiles.Length > 1 &&
                    logfiles[0].HiveHeader.Sequence1 >= logfiles[1].HiveHeader.Sequence1)
                {
                    (logfiles[0], logfiles[1]) = (logfiles[1], logfiles[0]);
                    (logs[0], logs[1]) = (logs[1], logs[0]);
                }

                // If hive header failed validation, recover from latest log
                if (headerSize == 0)
                {
                    var lastvalid = logfiles.LastOrDefault(static logfile => logfile.HeaderValid)
                        ?? throw new RegistryCorruptException("Registry transaction logs are corrupt");

                    _header = lastvalid.HiveHeader;
                }

                int lastSequenceNumber;
                int maxPosition;

                // First log
                if (logfiles.Length > 0 &&
                    logfiles[0].HiveHeader.Sequence1 >= _header.Sequence2)
                {
                    (lastSequenceNumber, maxPosition) = logfiles[0].UpdateHive(FileStream);

                    if (maxPosition > _header.Length)
                    {
                        _header.Length = maxPosition;
                    }

                    // Also a secondary log
                    if (logfiles.Length > 1 &&
                        logfiles[1].HiveHeader.Sequence1 > _header.Sequence2)
                    {
                        // If secondary log continues right after last record in first log
                        if (logfiles[1].HiveHeader.Sequence1 == lastSequenceNumber + 1)
                        {
                            (lastSequenceNumber, maxPosition) = logfiles[1].UpdateHive(FileStream);

                            if (maxPosition > _header.Length)
                            {
                                _header.Length = maxPosition;
                            }
                        }
                        else
                        {
                            // Otherwise secondary log is invalid, just get the latest sequence number
                            // without actually replay the records and zero out the log file
                            lastSequenceNumber = Math.Max(logfiles[1].HiveHeader.Sequence2, lastSequenceNumber);

                            if (logs[1].CanWrite)
                            {
                                logs[1].SetLength(0);
                            }
                        }
                    }
                }
                // If only secondary log file is after hive in sequence
                else if (logfiles.Length > 1 &&
                    logfiles[1].HiveHeader.Sequence1 >= _header.Sequence2)
                {
                    (lastSequenceNumber, maxPosition) = logfiles[1].UpdateHive(FileStream);

                    if (maxPosition > _header.Length)
                    {
                        _header.Length = maxPosition;
                    }
                }
                else
                {
                    throw new RegistryCorruptException("Registry transaction logs are corrupt");
                }

                // Store latest recovered sequence number in the hive header
                // and write this modified header to the hive file
                _header.Sequence1 = _header.Sequence2 = lastSequenceNumber + 1;
                _header.Timestamp = DateTime.UtcNow;
                _header.WriteTo(buffer);
                FileStream.Position = 0;
                FileStream.Write(buffer);
                FileStream.Position = 0;
            }
            else if (FileStream.CanWrite)
            {
                throw new RegistryCorruptException("Registry hive needs transaction logs to recover pending changes");
            }
        }

        if (ownership == Ownership.Dispose && logstreams is not null)
        {
            foreach (var log in logstreams)
            {
                log.Dispose();
            }
        }

        // Enumerate hbins
        _bins = [];
        var pos = 0;
        while (pos < _header.Length)
        {
            FileStream.Position = BinStart + pos;
            FileStream.ReadExactly(buffer[..BinHeader.HeaderSize]);
            var header = new BinHeader();
            header.ReadFrom(buffer);
            _bins.Add(header);

            pos += header.BinSize;
        }

        if (_bins.Count == 0)
        {
            throw new RegistryCorruptException("Registry hive is corrupt");
        }
    }

    /// <summary>
    /// Gets the root key in the registry hive.
    /// </summary>
    public RegistryKey Root
    {
        get
        {
            var cell = GetCell<KeyNodeCell>(_header.RootCell)
                ?? throw new RegistryCorruptException("Registry hive is corrupt");

            return new(this, cell);
        }
    }

    /// <summary>
    /// Disposes of this instance, freeing any underlying stream (if any).
    /// </summary>
    public void Dispose(bool disposing)
    {
        if (FileStream is not null)
        {
            if (disposing)
            {
                if (_ownsStream == Ownership.Dispose)
                {
                    FileStream.Dispose();
                }
                else if (FileStream.CanWrite)
                {
                    FileStream.Flush();
                }
            }

            FileStream = null;
        }
    }

    /// <summary>
    /// Creates a new (empty) registry hive.
    /// </summary>
    /// <param name="stream">The stream to contain the new hive.</param>
    /// <returns>The new hive.</returns>
    /// <remarks>
    /// The returned object does not assume ownership of the stream.
    /// </remarks>
    public static RegistryHive Create(Stream stream)
    {
        return Create(stream, Ownership.None);
    }

    /// <summary>
    /// Creates a new (empty) registry hive.
    /// </summary>
    /// <param name="stream">The stream to contain the new hive.</param>
    /// <param name="ownership">Whether the returned object owns the stream.</param>
    /// <returns>The new hive.</returns>
    public static RegistryHive Create(Stream stream, Ownership ownership)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream), "Attempt to create registry hive in null stream");
        }

        // Construct a file with minimal structure - hive header, plus one (empty) bin
        var binHeader = new BinHeader
        {
            FileOffset = 0,
            BinSize = 4 * Sizes.OneKiB
        };

        var hiveHeader = new HiveHeader
        {
            Length = binHeader.BinSize
        };

        stream.Position = 0;

        Span<byte> hiveHeaderBuffer = stackalloc byte[hiveHeader.Size];
        hiveHeader.WriteTo(hiveHeaderBuffer);
        stream.Write(hiveHeaderBuffer);

        Span<byte> binHeaderBuffer = stackalloc byte[binHeader.Size];
        binHeader.WriteTo(binHeaderBuffer);
        stream.Position = BinStart;
        stream.Write(binHeaderBuffer);

        Span<byte> sizeBuffer = stackalloc byte[4];
        EndianUtilities.WriteBytesLittleEndian(binHeader.BinSize - binHeader.Size, sizeBuffer);
        stream.Write(sizeBuffer);

        // Make sure the file is initialized out to the end of the first bin
        stream.Position = BinStart + binHeader.BinSize - 1;
        stream.WriteByte(0);

        // Temporary hive to perform construction of higher-level structures
        var newHive = new RegistryHive(stream, ownership);

        newHive.CreateRoot();

        // Finally, return the new hive
        return newHive;
    }

    private void CreateRoot()
    {
        var rootCell = new KeyNodeCell("root", -1)
        {
            Flags = RegistryKeyFlags.Normal | RegistryKeyFlags.Root
        };
        UpdateCell(rootCell, true);

        var sd = new RegistrySecurity("O:BAG:BAD:PAI(A;;KA;;;SY)(A;CI;KA;;;BA)");
        var secCell = new SecurityCell(sd);
        UpdateCell(secCell, true);
        secCell.NextIndex = secCell.Index;
        secCell.PreviousIndex = secCell.Index;
        UpdateCell(secCell, false);

        rootCell.SecurityIndex = secCell.Index;
        UpdateCell(rootCell, false);

        // Ref the root cell from the hive header
        Span<byte> hiveHeaderBuffer = stackalloc byte[HiveHeader.HeaderSize];
        _header.RootCell = rootCell.Index;
        _header.WriteTo(hiveHeaderBuffer);
        FileStream.Position = 0;
        FileStream.Write(hiveHeaderBuffer);
    }

    /// <summary>
    /// Creates a new (empty) registry hive.
    /// </summary>
    /// <param name="path">The file to create the new hive in.</param>
    /// <returns>The new hive.</returns>
    public static RegistryHive Create(string path)
    {
        var locator = new LocalFileLocator(string.Empty, useAsync: false);
        return Create(locator.Open(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None), Ownership.Dispose);
    }

    internal K GetCell<K>(int index)
        where K : Cell
    {
        var bin = GetBin(index);

        if (bin is not null)
        {
            return (K)bin.TryGetCell(index);
        }

        return null;
    }

    internal void FreeCell(int index)
    {
        var binIndex = FindBinIndex(index);
        if (binIndex < 0)
        {
            return;
        }

        var bin = LoadBin(_bins[binIndex]);
        bin.FreeCell(index);
        SetLargestFreeCell(binIndex, bin.LargestFreeCell);
    }

    internal int UpdateCell(Cell cell, bool canRelocate)
    {
        if (cell.Index == -1 && canRelocate)
        {
            cell.Index = AllocateRawCell(cell.Size);
        }

        var bin = GetBin(cell.Index);

        if (bin is not null)
        {
            if (bin.UpdateCell(cell))
            {
                return cell.Index;
            }

            if (canRelocate)
            {
                var oldCell = cell.Index;
                cell.Index = AllocateRawCell(cell.Size);
                bin = GetBin(cell.Index);
                if (!bin.UpdateCell(cell))
                {
                    cell.Index = oldCell;
                    throw new RegistryCorruptException("Failed to migrate cell to new location");
                }

                FreeCell(oldCell);
                return cell.Index;
            }

            throw new ArgumentException("Can't update cell, needs relocation but relocation disabled",
                nameof(canRelocate));
        }

        throw new RegistryCorruptException($"No bin found containing index: {cell.Index}");
    }

    internal Span<byte> RawCellData(int index, Span<byte> maxBytes, bool ignoreBigData = false)
    {
        var bin = GetBin(index);

        if (bin is not null)
        {
            return bin.ReadRawCellData(index, maxBytes, ignoreBigData);
        }

        return default;
    }

    internal bool WriteRawCellData(int index, ReadOnlySpan<byte> data)
    {
        var bin = GetBin(index);

        if (bin is not null)
        {
            return bin.WriteRawCellData(index, data);
        }

        throw new RegistryCorruptException($"No bin found containing index: {index}");
    }

    internal int AllocateRawCell(int capacity)
    {
        var minSize = MathUtilities.RoundUp(capacity + 4, 8); // Allow for size header and ensure multiple of 8

        // First fit in file order, as before, but the free-space tree jumps straight to the first bin that can
        // hold the cell (or that isn't loaded yet, so its free space is unknown) instead of trying every full
        // bin in front of it. The bin chosen, and the cell within it, are the same as the linear scan's.
        EnsureFreeSpaceTree();
        while (true)
        {
            var binIndex = FirstBinWithFreeCell(minSize);
            if (binIndex < 0)
            {
                break;
            }

            var bin = LoadBin(_bins[binIndex]);
            var cellIndex = bin.AllocateCell(minSize);
            SetLargestFreeCell(binIndex, bin.LargestFreeCell);

            if (cellIndex >= 0)
            {
                return cellIndex;
            }
        }

        var newBinHeader = AllocateBin(minSize);
        var newBin = LoadBin(newBinHeader);
        var newCellIndex = newBin.AllocateCell(minSize);
        EnsureFreeSpaceTree();
        SetLargestFreeCell(_bins.Count - 1, newBin.LargestFreeCell);
        return newCellIndex;
    }

    /// <summary>
    /// Max segment tree over <see cref="_bins"/> (leaves at [_freeSpaceLeaves + i]): for each bin, the size of its
    /// largest free cell, <see cref="int.MaxValue"/> while the bin hasn't been loaded (unknown, so it must be
    /// looked at), and -1 for padding leaves past the last bin. Lets <see cref="AllocateRawCell"/> find the first
    /// bin that can satisfy a request in O(log n) instead of trying every bin from the start.
    /// </summary>
    private int[] _freeSpaceTree;

    private int _freeSpaceLeaves;

    /// <summary>How many of <see cref="_bins"/> the tree has leaves for (bins appended later are padding until added).</summary>
    private int _freeSpaceBins;

    private void EnsureFreeSpaceTree()
    {
        if (_freeSpaceTree is not null && _freeSpaceLeaves >= _bins.Count)
        {
            // Bins appended by AllocateBin since the last call are still padding: mark them "unknown".
            while (_freeSpaceBins < _bins.Count)
            {
                SetLargestFreeCell(_freeSpaceBins++, int.MaxValue);
            }

            return;
        }

        var leaves = 1;
        while (leaves < _bins.Count)
        {
            leaves <<= 1;
        }

        var tree = new int[2 * leaves];
        for (var i = 0; i < leaves; i++)
        {
            tree[leaves + i] = i >= _bins.Count
                ? -1
                : _freeSpaceTree is not null && i < _freeSpaceLeaves && _freeSpaceTree[_freeSpaceLeaves + i] >= 0
                    ? _freeSpaceTree[_freeSpaceLeaves + i]
                    : int.MaxValue;
        }

        for (var i = leaves - 1; i >= 1; i--)
        {
            tree[i] = Math.Max(tree[2 * i], tree[2 * i + 1]);
        }

        _freeSpaceTree = tree;
        _freeSpaceLeaves = leaves;
        _freeSpaceBins = _bins.Count;
    }

    private void SetLargestFreeCell(int binIndex, int largestFreeCell)
    {
        if (_freeSpaceTree is null)
        {
            // Not built yet: built on the first allocation, from the bins loaded by then.
            return;
        }

        if (binIndex >= _freeSpaceBins)
        {
            // A bin the tree doesn't cover yet (appended since): grow/mark first, then store the real value.
            EnsureFreeSpaceTree();
        }

        var i = _freeSpaceLeaves + binIndex;
        _freeSpaceTree[i] = largestFreeCell;
        for (i >>= 1; i >= 1; i >>= 1)
        {
            _freeSpaceTree[i] = Math.Max(_freeSpaceTree[2 * i], _freeSpaceTree[2 * i + 1]);
        }
    }

    /// <summary>The lowest bin index whose largest free cell is at least <paramref name="size"/> (or unknown), or -1.</summary>
    private int FirstBinWithFreeCell(int size)
    {
        if (_freeSpaceTree[1] < size)
        {
            return -1;
        }

        var i = 1;
        while (i < _freeSpaceLeaves)
        {
            i = _freeSpaceTree[2 * i] >= size ? 2 * i : 2 * i + 1;
        }

        return i - _freeSpaceLeaves;
    }

    private BinHeader? FindBin(int index)
    {
        var binIndex = FindBinIndex(index);
        return binIndex < 0 ? null : _bins[binIndex];
    }

    private int FindBinIndex(int index)
    {
        if (index < 0)
        {
            return -1;
        }

        // Bins are contiguous and sorted by FileOffset. Binary search for the first bin whose end
        // (FileOffset + BinSize) is >= index, then require that it starts at or before index - the same
        // bin the previous linear TakeWhile/FirstOrDefault scan returned, in O(log n) instead of O(n).
        int lo = 0, hi = _bins.Count - 1, found = -1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            var bin = _bins[mid];
            if (bin.FileOffset + bin.BinSize >= index)
            {
                found = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }

        if (found < 0 || _bins[found].FileOffset > index)
        {
            return -1;
        }

        return found;
    }

    private Bin GetBin(int cellIndex)
    {
        var binHeader = FindBin(cellIndex);

        if (binHeader is not null)
        {
            return LoadBin(binHeader.Value);
        }

        return null;
    }

    /// <summary>
    /// Returns the <see cref="Bin"/> for a bin, reading and parsing it only the first time.
    /// <para>
    /// Every cell read, write and allocation goes through a Bin, and loading one reads the whole bin
    /// from the stream and scans all its cells for free space. Building a fresh Bin on every access
    /// made writes to large hives very slow: <see cref="AllocateRawCell"/> walks the bins from the start,
    /// so one allocation in a 16 MiB hive whose early bins are full re-read megabytes. Caching is safe
    /// because after construction all changes to bin contents go through the cached Bin, which keeps its
    /// buffer, its free-cell list and the stream in step; the only other writes are to the hive header
    /// and to a new bin in <see cref="AllocateBin"/> before it is first loaded.
    /// </para>
    /// </summary>
    private Bin LoadBin(BinHeader binHeader)
    {
        if (_binCache.TryGetValue(binHeader.FileOffset, out var cached))
        {
            return cached;
        }

        FileStream.Position = BinStart + binHeader.FileOffset;
        var bin = new Bin(this, FileStream);
        _binCache.Add(binHeader.FileOffset, bin);
        return bin;
    }

    private BinHeader AllocateBin(int minSize)
    {
        var lastBin = _bins[_bins.Count - 1];

        var newBinHeader = new BinHeader
        {
            FileOffset = lastBin.FileOffset + lastBin.BinSize
        };
        newBinHeader.BinSize = MathUtilities.RoundUp(minSize + newBinHeader.Size, 4 * Sizes.OneKiB);

        var buffer = ArrayPool<byte>.Shared.Rent(newBinHeader.Size);
        try
        {
            newBinHeader.WriteTo(buffer);
            FileStream.Position = BinStart + newBinHeader.FileOffset;
            FileStream.Write(buffer, 0, newBinHeader.Size);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        Span<byte> cellHeader = stackalloc byte[4];
        EndianUtilities.WriteBytesLittleEndian(newBinHeader.BinSize - newBinHeader.Size, cellHeader);
        FileStream.Write(cellHeader);

        // Update hive with new length
        _header.Length = newBinHeader.FileOffset + newBinHeader.BinSize;
        _header.Timestamp = DateTime.UtcNow;
        _header.Sequence1++;
        _header.Sequence2++;
        FileStream.Position = 0;
        Span<byte> hiveHeader = stackalloc byte[_header.Size];
        FileStream.ReadExactly(hiveHeader);
        _header.WriteTo(hiveHeader);
        FileStream.Position = 0;
        FileStream.Write(hiveHeader);

        // Make sure the file is initialized to desired position
        FileStream.Position = BinStart + _header.Length - 1;
        FileStream.WriteByte(0);

        _bins.Add(newBinHeader);
        return newBinHeader;
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    ~RegistryHive()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}