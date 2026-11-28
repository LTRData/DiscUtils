namespace DiscUtils.ReFs.Mount.Enums;

/// <summary>
///     The flags of the boot sector (u32 at 0x2C). The names are ours; the meanings are inferred from how Windows uses each bit: the mount reads 0x01, 0x02, 0x20 and 0x40,
///     a boot sector update sets 0x10, and a version upgrade sets and clears 0x80. Windows does not check the flags otherwise. forefst measured 0x06 on ReFS 3.4, 0x26 on 3.7 and 3.9
///     and 0x66
///     from 3.10.
/// </summary>
[Flags]
internal enum BootSectorFeatures : uint
{
    None = 0,

    /// <summary>Read at mount: sets bit 0x80 of the volume's state word. What that bit gates is unknown. Not set on any volume seen.</summary>
    Bit0x01 = 0x01,

    /// <summary>
    ///     Read at mount: when 0x01 is clear, clears bit 0x80 of the volume's state word. Windows never sets it afterwards; set on every volume
    ///     measured.
    /// </summary>
    Bit0x02 = 0x02,

    /// <summary>Not used by Windows when reading the boot sector; set on every volume measured. Meaning unknown.</summary>
    Bit0x04 = 0x04,

    /// <summary>Set when Windows rewrites the sector for a key management update (presumably volume encryption keys). Meaning inferred only.</summary>
    KmeUpdated = 0x10,

    /// <summary>
    ///     Hard links are supported: the mount records this bit in the volume state and the link, rename and
    ///     cleanup paths test it. Set by formats from ReFS 3.7.
    /// </summary>
    HardLinksSupported = 0x20,

    /// <summary>
    ///     The checksum type byte at 0x2A is valid: the mount uses that byte when this bit is set and otherwise assumes type 2 (CRC-64). Set by formats from
    ///     ReFS 3.10; not set on an upgraded volume.
    /// </summary>
    ChecksumTypeValid = 0x40,

    /// <summary>
    ///     The volume is marked for a version upgrade: Windows sets it when an upgrade is requested (an error when already set) and the next mount writes the
    ///     higher version to 0x28 and clears it. The name is inferred from that behaviour.
    /// </summary>
    MarkedForUpgrade = 0x80
}