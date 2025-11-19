// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

namespace DiscUtils.ExFat.Internal;

/// <summary>
/// Options for partition format
/// </summary>
public class ExFatFormatOptions
{
    /// <summary>
    /// Gets or sets the bytes per sector.
    /// </summary>
    /// <value>
    /// The bytes per sector.
    /// </value>
    public uint BytesPerSector { get; set; } = 512;

    /// <summary>
    /// Gets or sets the sectors per cluster.
    /// If not provided, the value is computed.
    /// </summary>
    /// <value>
    /// The sectors per cluster.
    /// </value>
    public uint? SectorsPerCluster { get; set; }

    /// <summary>
    /// Gets or sets the volume space.
    /// If not provided, the volume space is the partition stream length (which is a good idea)
    /// </summary>
    /// <value>
    /// The volume space.
    /// </value>
    public ulong? VolumeSpace { get; set; }
}
