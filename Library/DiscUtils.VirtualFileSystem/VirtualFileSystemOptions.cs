namespace DiscUtils.VirtualFileSystem;

public class VirtualFileSystemOptions : DiscFileSystemOptions
{
    public bool CanWrite { get; set; } = true;

    public bool HasSecurity { get; set; }

    public bool IsThreadSafe { get; set; }

    public bool CaseSensitive { get; set; }

    public string? VolumeLabel { get; set; }
}