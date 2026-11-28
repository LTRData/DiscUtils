using DiscUtils.Streams;

namespace DiscUtils.ReFs;

/// <summary>
/// Lightweight, non-mutating recognition of the ReFS boot sector. Used by detection only;
/// opening performs deeper validation (superblock, checkpoint, checksums).
/// </summary>
internal static class ReFsBootSector
{
    internal const int Length = 0x28;

    // Field offsets, see the evidence notes for the reader this port is based on.
    private const int SignatureOffset = 3;
    private const int StructureSizeOffset = 0x14;
    private const int BytesPerSectorOffset = 0x20;
    private const int SectorsPerClusterOffset = 0x24;

    private static ReadOnlySpan<byte> Signature => "ReFS\0\0\0\0"u8;

    internal static bool Detect(Stream stream)
    {
        if (stream.Length < 4096)
        {
            return false;
        }

        stream.Position = 0;
        Span<byte> data = stackalloc byte[Length];
        stream.ReadExactly(data);

        return IsBootSector(data);
    }

    internal static bool IsBootSector(ReadOnlySpan<byte> data)
    {
        if (data.Length < Length || !data.Slice(SignatureOffset, 8).SequenceEqual(Signature))
        {
            return false;
        }

        if (EndianUtilities.ToUInt16LittleEndian(data[StructureSizeOffset..]) < 0x58)
        {
            return false;
        }

        var bytesPerSector = EndianUtilities.ToUInt32LittleEndian(data[BytesPerSectorOffset..]);

        if (bytesPerSector is < 512 or > 4096 || (bytesPerSector & (bytesPerSector - 1)) != 0)
        {
            return false;
        }

        return EndianUtilities.ToUInt32LittleEndian(data[SectorsPerClusterOffset..]) is 1 or 4 or 8 or 16 or 32 or 64 or 128 or 256 or 512;
    }
}