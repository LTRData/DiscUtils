using DiscUtils.ReFs.Diagnostics.Enums;

namespace DiscUtils.ReFs.Diagnostics;

// Experimental diagnostics for tools: details of a ReFS volume that the generic file system API has no place for. The shapes follow what
// is known about the format and may change between releases; values whose meaning is inferred are exposed as raw numbers plus a display
// string rather than as named enums.

/// <summary>Volume-wide details of a ReFS volume (experimental).</summary>
/// <param name="FormatVersion">The on-disk format version from the checkpoint.</param>
/// <param name="BootSectorVersion">The format version in the boot sector.</param>
/// <param name="SecondaryCheckpointVersion">A second version pair in the checkpoint; its role is not established.</param>
/// <param name="ClusterSize">Bytes per cluster.</param>
/// <param name="TotalClusters">Clusters in the volume.</param>
/// <param name="Serial">The volume serial number.</param>
/// <param name="VolumeId">The volume GUID from the superblock.</param>
/// <param name="MetadataChecksum">The checksum of metadata pages.</param>
/// <param name="DataChecksum">The checksum integrity streams use on this volume; <see cref="ReFsChecksumKind.None"/> when not supported.</param>
/// <param name="DataChecksumBlockSize">Bytes covered by one integrity-stream checksum.</param>
/// <param name="Label">The volume label.</param>
/// <param name="Created">When the volume was formatted.</param>
/// <param name="LastMounted">When the volume was last mounted for writing.</param>
/// <param name="LastMountedByVersion">The file system version of the Windows that last mounted it.</param>
/// <param name="VolumeVersion">The format version recorded with the volume information.</param>
/// <param name="VolumeFlags">The raw volume information flags.</param>
/// <param name="CheckpointClock">The virtual clock of the checkpoint in use.</param>
/// <param name="RestartClock">The restart clock of the checkpoint in use (compared with redo records on replay).</param>
/// <param name="CheckpointFlags">The raw checkpoint flags.</param>
/// <param name="CheckpointFlagNames">The checkpoint flags as names, with unknown bits in hex.</param>
/// <param name="DeveloperVolume">True for a Dev Drive.</param>
/// <param name="ReplayEnabled">True when the redo log was applied in memory on open.</param>
public sealed record ReFsVolumeDetails(
    Version FormatVersion,
    Version? BootSectorVersion,
    Version? SecondaryCheckpointVersion,
    uint ClusterSize,
    ulong TotalClusters,
    ulong Serial,
    Guid VolumeId,
    ReFsChecksumKind MetadataChecksum,
    ReFsChecksumKind DataChecksum,
    int DataChecksumBlockSize,
    string Label,
    DateTimeOffset Created,
    DateTimeOffset LastMounted,
    Version LastMountedByVersion,
    Version VolumeVersion,
    uint VolumeFlags,
    ulong CheckpointClock,
    ulong RestartClock,
    uint CheckpointFlags,
    string CheckpointFlagNames,
    bool DeveloperVolume,
    bool ReplayEnabled);