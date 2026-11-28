namespace DiscUtils.ReFs.Compression.Enums;

[Flags]
internal enum CompressionHeaderFlags : uint
{
    None = 0,
    HasChecksums = 2
}