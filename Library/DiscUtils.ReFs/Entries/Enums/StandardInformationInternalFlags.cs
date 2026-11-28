namespace DiscUtils.ReFs.Entries.Enums;

/// <summary>
///     Internal flag word at offset 0x24 of <c>$STANDARD_INFORMATION</c>. Windows builds it from the file's in-memory state and reads it back when
///     it loads the file (bits 0x1, 0x2, 0x4 and the 0x30 field; 0x8 is not read back). Bits from 0x40 up are never written.
/// </summary>
[Flags]
internal enum StandardInformationInternalFlags : uint
{
    None = 0,

    /// <summary>
    ///     The file is deleted (name inferred): at load Windows marks all of the file's streams deleted.
    /// </summary>
    Deleted = 0x1,

    /// <summary>
    ///     Directory lookups match exact spelling only (per-directory case sensitivity, as set with <c>fsutil file setCaseSensitiveInfo</c>).
    /// </summary>
    CaseSensitiveDirectory = 0x2,

    /// <summary>
    ///     Meaning unknown. Read back at load, where it makes Windows reload the security descriptor and scan the attributes.
    /// </summary>
    Bit0x4 = 0x4,

    /// <summary>
    ///     Meaning unknown: set when the file is created. Written but not read back at load.
    /// </summary>
    Bit0x8 = 0x8,

    /// <summary>
    ///     A two-bit field (bits 4 and 5): the redirection trust level of a symbolic link or mount point, as Windows computes it when the reparse
    ///     point is set (<c>(word &gt;&gt; 4) &amp; 3</c>). Values 0 to 3.
    /// </summary>
    RedirectionTrustLevelMask = 0x30
}