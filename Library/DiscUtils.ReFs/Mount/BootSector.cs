using DiscUtils.ReFs.Checksums;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Mount.Enums;

namespace DiscUtils.ReFs.Mount;

/// <summary>
///     ReFS boot sector. Offsets are derived from observed mount behaviour of Windows, with names and values taken from the forefst documentation
///     where the behaviour does not show them (marked).
///     <list type="table">
///         <listheader>
///             <term>Offset, size</term><description>Field</description>
///         </listheader>
///         <item>
///             <term>0x00, 3</term><description>Jump instruction, zero (not read)</description>
///         </item>
///         <item>
///             <term>0x03, 8</term><description>File system name "ReFS" and four zero bytes (checked)</description>
///         </item>
///         <item>
///             <term>0x10, 4</term><description>"FSRS"</description>
///         </item>
///         <item>
///             <term>0x14, 2</term><description>Structure size, 0x200 on every volume seen; the checksum covers this many bytes</description>
///         </item>
///         <item>
///             <term>0x16, 2</term><description>Checksum: 16-bit rotate right and add over bytes 3 to the structure size, without these two bytes</description>
///         </item>
///         <item>
///             <term>0x18, 8</term><description>Total sectors</description>
///         </item>
///         <item>
///             <term>0x20, 4</term><description>Bytes per sector (512, or 4096 on a 4Kn disk)</description>
///         </item>
///         <item>
///             <term>0x24, 4</term><description>Sectors per cluster</description>
///         </item>
///         <item>
///             <term>0x28, 1 and 0x29, 1</term>
///             <description>
///                 Major and minor version, copied by the mount to the volume (version 3.8 is the threshold for extended attributes and 3.11 for encryption).
///                 Windows writes it back only when mounting a volume marked for upgrade (flag 0x80) and the upgrade version is higher, so it is
///                 not always the version the volume was formatted with.
///             </description>
///         </item>
///         <item>
///             <term>0x2A, 1</term>
///             <description>
///                 Checksum type the volume was formatted with (<see cref="Checksums.ChecksumKind" />), read as a byte and only when flag 0x40 is set;
///                 otherwise Windows assumes 2. No range check at mount. Not changed by an upgrade, so the checkpoint decides what is really on disk.
///             </description>
///         </item>
///         <item>
///             <term>0x2C, 4</term><description>Flags (<see cref="BootSectorFeatures" />)</description>
///         </item>
///         <item>
///             <term>0x30, 8</term><description>Zero</description>
///         </item>
///         <item>
///             <term>0x38, 8</term><description>Volume serial number</description>
///         </item>
///         <item>
///             <term>0x40, 8</term><description>Bytes per container, 64 MiB on every volume seen</description>
///         </item>
///         <item>
///             <term>0x48, 16</term>
///             <description>16 bytes copied by the mount into the volume state (their use is unknown); forefst: zero on volumes formatted before 3.10 (also after an upgrade)</description>
///         </item>
///     </list>
/// </summary>
internal readonly record struct BootSector
{
    private const int MinimumStructureSize = 0x58;

    /// <summary>Unpacks and validates a boot sector.</summary>
    /// <exception cref="ReFsFormatException">The bytes are not a valid ReFS boot sector.</exception>
    public BootSector(ReadOnlySpan<byte> sector)
    {
        // In address order, the cheap checks first; the checksum over the whole structure comes last.
        if (sector.Length < MinimumStructureSize)
        {
            throw new ReFsFormatException("Invalid ReFS boot sector: sector too short");
        }

        if (!sector.Slice(0x03, 8).SequenceEqual("ReFS\0\0\0\0"u8))
        {
            throw new ReFsFormatException("Invalid ReFS boot sector: missing 'ReFS' signature");
        }

        StructureSize = sector.U16(0x14);

        if (StructureSize < MinimumStructureSize || StructureSize > sector.Length)
        {
            throw new ReFsFormatException($"Invalid ReFS boot sector: bad structure size {StructureSize}");
        }

        StoredChecksum = sector.U16(0x16);

        TotalSectors = sector.U64(0x18);

        if (TotalSectors == 0)
        {
            throw new ReFsFormatException("Invalid ReFS boot sector: zero volume size");
        }

        BytesPerSector = sector.U32(0x20);

        if (BytesPerSector is < 512 or > 4096 || !uint.IsPow2(BytesPerSector))
        {
            throw new ReFsFormatException($"Invalid ReFS boot sector: unsupported bytes per sector {BytesPerSector}");
        }

        SectorsPerCluster = sector.U32(0x24);

        if (SectorsPerCluster is not (1 or 4 or 8 or 16 or 32 or 64 or 128 or 256 or 512))
        {
            throw new ReFsFormatException($"Invalid ReFS boot sector: unsupported sectors per cluster {SectorsPerCluster}");
        }

        MajorVersion = sector[0x28];
        MinorVersion = sector[0x29];
        ChecksumType = (ChecksumKind)sector[0x2A];
        Flags = (BootSectorFeatures)sector.U32(0x2C);
        VolumeSerial = sector.U64(0x38);
        BytesPerContainer = sector.U64(0x40);
        ExtendedId = new Guid(sector.Slice(0x48, 16).ToArray());

        if (StoredChecksum != Checksum(sector, StructureSize))
        {
            throw new ReFsFormatException("Invalid ReFS boot sector: checksum mismatch");
        }
    }

    /// <summary>u16 @0x14: the length the checksum covers.</summary>
    public ushort StructureSize { get; }

    /// <summary>u16 @0x16.</summary>
    public ushort StoredChecksum { get; }

    /// <summary>u64 @0x18.</summary>
    public ulong TotalSectors { get; }

    /// <summary>u32 @0x20.</summary>
    public uint BytesPerSector { get; }

    /// <summary>u32 @0x24.</summary>
    public uint SectorsPerCluster { get; }

    /// <summary>u8 @0x28.</summary>
    public byte MajorVersion { get; }

    /// <summary>u8 @0x29.</summary>
    public byte MinorVersion { get; }

    /// <summary>u8 @0x2A: the checksum type the volume was formatted with; meaningful only with <see cref="BootSectorFeatures.ChecksumTypeValid" />.</summary>
    public ChecksumKind ChecksumType { get; }

    /// <summary>u32 @0x2C.</summary>
    public BootSectorFeatures Flags { get; }

    /// <summary>u64 @0x38.</summary>
    public ulong VolumeSerial { get; }

    /// <summary>u64 @0x40: the size of a container (the unit of the virtual to physical cluster translation).</summary>
    public ulong BytesPerContainer { get; }

    /// <summary>16 bytes @0x48.</summary>
    public Guid ExtendedId { get; }

    public uint ClusterSize => BytesPerSector * SectorsPerCluster;

    public ulong TotalClusters => TotalSectors / SectorsPerCluster;

    public static bool TryParse(ReadOnlySpan<byte> sector, out BootSector result, out string? error)
    {
        try
        {
            result = new BootSector(sector);
            error = null;
            return true;
        }
        catch (ReFsFormatException ex)
        {
            result = default;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>16-bit rotate-right-and-add over bytes [3, size), skipping the checksum field itself (offsets 0x16, 0x17).</summary>
    internal static ushort Checksum(ReadOnlySpan<byte> sector, int structureSize)
    {
        ushort value = 0;

        for (var i = 3; i < structureSize; i++)
        {
            if (i is 0x16 or 0x17)
            {
                continue;
            }

            value = (ushort)(((value >> 1) | (value << 15)) + sector[i]);
        }

        return value;
    }
}