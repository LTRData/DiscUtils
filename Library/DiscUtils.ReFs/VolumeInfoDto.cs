using DiscUtils.ReFs.Mount;
using DiscUtils.ReFs.Checksums;
using DiscUtils.ReFs.Mount.Enums;

namespace DiscUtils.ReFs;

/// <summary>
///     Summary of a mounted volume. <c>LastShutdownWasDirty</c> is a legacy, unverified payload-absence heuristic;
///     neither value establishes actual shutdown state or whether replay is needed.
/// </summary>
internal sealed record VolumeInfoDto(
    Version FormatVersion,
    uint ClusterSize,
    ulong TotalClusters,
    ulong Serial,
    Guid VolumeId,
    ulong CheckpointClock,
    bool LastShutdownWasDirty = false,
    CheckpointFeatures CheckpointFeatures = CheckpointFeatures.None,
    Version? BootSectorVersion = null,
    Version? SecondaryCheckpointVersion = null,
    ChecksumKind MetadataChecksum = ChecksumKind.None,
    ChecksumKind DataChecksum = ChecksumKind.None,
    int DataChecksumBlockSize = 0)
{
    // MetadataChecksum: the kind in the page references (the object table root's). DataChecksum and DataChecksumBlockSize: what integrity-stream
    // data uses on this volume, derived from it as Windows does; None when this reader does not support the configuration.
}