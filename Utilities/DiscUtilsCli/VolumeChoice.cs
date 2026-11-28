namespace DiscUtilsCli;

/// <summary>A volume of an image: its index (or <see cref="OpenedImage.WholeDisk" />), place and size in bytes, type, and detected file system.</summary>
internal sealed record VolumeChoice(int Index, string Identity, long Start, long Length, string Type, string? FileSystem);