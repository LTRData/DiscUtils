using DiscUtils.ReFs.Checksums;
using DiscUtils.ReFs.Compat;
#if NET46
using DiscUtils.Streams.Compatibility;
#endif

namespace DiscUtils.ReFs.Mount;

/// <summary>
///     Pointer to a metadata page: up to four consecutive cluster numbers (normally container-space LCNs; physical for container trees)
///     plus the checksum of the page contents (the digest
///     lives in the parent pointer, not in the page). It is a four-LCN tuple plus a checksum blob; forefst calls it a page reference. The byte at 0x23 is 8 and the
///     digest length is at most 0x20; the kind byte at 0x22 and the flags at 0x20 are from the images and forefst.
///     <list type="table">
///         <listheader>
///             <term>Offset, size</term><description>Field</description>
///         </listheader>
///         <item>
///             <term>0x00, 8 each</term><description>Four cluster numbers; unused ones are 0 (a 64 KiB-cluster volume uses one, a 4 KiB one four)</description>
///         </item>
///         <item>
///             <term>0x20, 2</term><description>Flags, 0 on every reference seen</description>
///         </item>
///         <item>
///             <term>0x22, 1</term><description>Checksum kind (<see cref="ChecksumKind" />)</description>
///         </item>
///         <item>
///             <term>0x23, 1</term><description>Offset of the digest from 0x20: 8</description>
///         </item>
///         <item>
///             <term>0x24, 4</term><description>Digest length: 4 (CRC-32C), 8 (CRC-64, XXH64) or 32 (SHA-256)</description>
///         </item>
///         <item>
///             <term>0x28</term><description>The digest</description>
///         </item>
///     </list>
///     The reference is stored in three sizes, which differ only in what follows the digest: 0x30 bytes (ReFS 3.10 and later, digests up to 8 bytes), 0x68
///     bytes (3.4 to 3.9: an 8-byte digest and 56 bytes of padding) and 0x48 bytes (SHA-256). The header says where the digest is and how long;
///     checkpoint offset 0x5C describes its self-checksum record, not the individual root pointers.
/// </summary>
internal readonly record struct PageRef
{
    /// <summary>Bytes before the digest: the cluster numbers and the checksum header.</summary>
    public const int MinimumLength = 0x28;

    /// <summary>The longest digest (SHA-256).</summary>
    public const int MaximumDigestLength = 32;

    // The digest bytes live in the struct (no array per reference); only the first DigestLength are meaningful, the rest are zero.
    private readonly byte[]? _digest;

    /// <summary>Unpacks a page reference.</summary>
    /// <exception cref="ReFsFormatException">The bytes are too short for the header or the digest it describes.</exception>
    public PageRef(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < MinimumLength)
        {
            throw new ReFsFormatException($"Page pointer too short ({bytes.Length} bytes).");
        }

        Lcn0 = bytes.U64(0x00);
        Lcn1 = bytes.U64(0x08);
        Lcn2 = bytes.U64(0x10);
        Lcn3 = bytes.U64(0x18);
        Flags = bytes.U16(0x20);
        Kind = (ChecksumKind)bytes[0x22];
        DigestOffset = bytes[0x23];
        var digestLength = bytes.U32(0x24);

        // The digest offset counts from the checksum header at 0x20.
        if (DigestOffset < 8 || digestLength > MaximumDigestLength || bytes.Length < 0x20 + DigestOffset + (int)digestLength)
        {
            throw new ReFsFormatException($"Unsupported page pointer digest (offset {DigestOffset}, length {digestLength}).");
        }

        DigestLength = digestLength;

        _digest = bytes.Slice(0x20 + DigestOffset, (int)digestLength).ToArray();
    }

    public ulong Lcn0 { get; }

    public ulong Lcn1 { get; }

    public ulong Lcn2 { get; }

    public ulong Lcn3 { get; }

    /// <summary>u16 @0x20: 0 on every reference seen.</summary>
    public ushort Flags { get; }

    /// <summary>u8 @0x22.</summary>
    public ChecksumKind Kind { get; }

    /// <summary>u8 @0x23: the digest's offset from 0x20 (8 on every reference seen).</summary>
    public byte DigestOffset { get; }

    /// <summary>u32 @0x24.</summary>
    public uint DigestLength { get; }

    /// <summary>The digest (<see cref="DigestLength" /> bytes, as stored).</summary>
    public ReadOnlySpan<byte> Digest => _digest is null ? default : _digest.AsSpan(0, (int)DigestLength);

    /// <summary>Number of clusters addressed (leading non-zero entries, at least 1).</summary>
    public int ClusterCount => Lcn1 == 0 ? 1 : Lcn2 == 0 ? 2 : Lcn3 == 0 ? 3 : 4;

    public bool Equals(PageRef other) =>
        Lcn0 == other.Lcn0 && Lcn1 == other.Lcn1 && Lcn2 == other.Lcn2 && Lcn3 == other.Lcn3
        && Flags == other.Flags && Kind == other.Kind && DigestOffset == other.DigestOffset
        && DigestLength == other.DigestLength && Digest.SequenceEqual(other.Digest);

    public ulong GetLcn(int index) =>
        index switch
        {
            0 => Lcn0,
            1 => Lcn1,
            2 => Lcn2,
            3 => Lcn3,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };


    /// <summary>True when <paramref name="data" /> matches the stored digest.</summary>
    public bool Verify(ReadOnlySpan<byte> data) => PageChecksum.Verify(Kind, data, Digest);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Lcn0);
        hash.Add(Lcn1);
        hash.Add(Lcn2);
        hash.Add(Lcn3);
        hash.Add(Flags);
        hash.Add(Kind);
        hash.Add(DigestOffset);
        hash.Add(DigestLength);

        foreach (var b in Digest)
        {
            hash.Add(b);
        }

        return hash.ToHashCode();
    }
}