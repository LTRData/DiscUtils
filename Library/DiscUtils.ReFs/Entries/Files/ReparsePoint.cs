using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Entries.Enums;

namespace DiscUtils.ReFs.Entries.Files;

/// <summary>
///     The reparse point row of a file record (<c>$REPARSE_POINT</c>, embedded type 0xC0, schema 0x1C0; key {u64 length, u32 0x80000001, u16 0xC0, u16 0}). Layout from the 0xC0 rows
///     of the test images, checked against forefst's page on <c>$REPARSE_POINT</c>:
///     0x00 u32 zero, 0x04 u32 length of the buffer (8 plus the data length; the row is padded to 8 bytes after it), 0x08 u32 offset of the buffer (0x0C), then the
///     Windows <c>REPARSE_DATA_BUFFER</c> header at that offset: u32 tag, u16 data length, u16 reserved (0), then the tag's data.
///     A third-party tag (no <see cref="ReparseTagFlags.Microsoft" /> bit) has a <c>REPARSE_GUID_DATA_BUFFER</c> instead: the same header, then a 16-byte GUID
///     naming the owner at buffer +8, then the data; the data length does not count the GUID.
///     Only two tags are interpreted (<see cref="ReFsReparsePointDto.MountPointTag" /> and <see cref="ReFsReparsePointDto.SymbolicLinkTag" />); every other is stored verbatim.
/// </summary>
internal sealed record ReparsePoint
{
    private const int RowHeaderLength = 12;
    private const int BufferHeaderLength = 8;
    private const int GuidLength = 16;

    /// <summary>Unpacks a reparse point row value.</summary>
    /// <exception cref="ReFsFormatException">The row is too short or its buffer lies outside it.</exception>
    public ReparsePoint(ReadOnlySpan<byte> value)
    {
        if (value.Length < RowHeaderLength + BufferHeaderLength)
        {
            throw new ReFsFormatException("Malformed reparse point row.");
        }

        var bufferLength = value.U32(4);
        var bufferOffset = value.U32(8);

        if (bufferOffset < RowHeaderLength || bufferLength < BufferHeaderLength || bufferOffset + (ulong)bufferLength > (ulong)value.Length)
        {
            throw new ReFsFormatException($"Reparse point buffer (offset {bufferOffset}, length {bufferLength}) does not fit its {value.Length}-byte row.");
        }

        var buffer = value.Slice((int)bufferOffset, (int)bufferLength);
        Tag = buffer.U32(0);
        uint dataLength = buffer.U16(4);
        var dataOffset = BufferHeaderLength;

        if (IsThirdParty)
        {
            if (buffer.Length < BufferHeaderLength + GuidLength)
            {
                throw new ReFsFormatException($"Third-party reparse tag 0x{Tag:X8} has no room for its GUID.");
            }

            ReparseGuid = new Guid(buffer.Slice(BufferHeaderLength, GuidLength).ToArray());
            dataOffset += GuidLength;
        }

        if (dataLength > buffer.Length - dataOffset)
        {
            throw new ReFsFormatException($"Reparse data length {dataLength} exceeds its buffer.");
        }

        Data = buffer.Slice(dataOffset, (int)dataLength).ToArray();
    }

    /// <summary>The reparse tag (<c>IO_REPARSE_TAG_*</c>); also stored at offset 0x54 of the standard information.</summary>
    public uint Tag { get; }

    /// <summary>The property bits of <see cref="Tag" />.</summary>
    public ReparseTagFlags TagFlags => (ReparseTagFlags)Tag & (ReparseTagFlags.Directory | ReparseTagFlags.NameSurrogate | ReparseTagFlags.HighLatency | ReparseTagFlags.Microsoft);

    /// <summary>The tag is not Microsoft's, so the buffer carries <see cref="ReparseGuid" />.</summary>
    public bool IsThirdParty => !TagFlags.HasFlag(ReparseTagFlags.Microsoft);

    /// <summary>The GUID of a third-party tag's owner (buffer +8); null for Microsoft tags.</summary>
    public Guid? ReparseGuid { get; }

    /// <summary>The tag's data (what follows the buffer header).</summary>
    public byte[] Data { get; }

    /// <summary>Unpacks a row, or returns null when it is malformed.</summary>
    public static ReparsePoint? TryParse(ReadOnlySpan<byte> value)
    {
        try
        {
            return new ReparsePoint(value);
        }
        catch (ReFsFormatException)
        {
            return null;
        }
    }
}