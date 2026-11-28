namespace DiscUtils.ReFs.Entries.Enums;

/// <summary>The flags byte of an extended attribute (<c>FILE_FULL_EA_INFORMATION.Flags</c>); Windows defines a single flag.</summary>
[Flags]
internal enum ReFsExtendedAttributeOptions : byte
{
    None = 0,

    /// <summary><c>FILE_NEED_EA</c>: the application that reads the file must understand this extended attribute.</summary>
    NeedEa = 0x80
}