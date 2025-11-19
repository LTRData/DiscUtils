// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;

namespace DiscUtils.ExFat.Internal.Partition.Entries;
/// <summary>
/// Flags for secondary entries (common flags)
/// </summary>
[Flags]
public enum ExFatGeneralSecondaryFlags : byte
{
    /// <summary>
    /// Indicates that allocation is possible (unofficial specs says it must always be set)
    /// </summary>
    ClusterAllocationPossible = 0x01,
    /// <summary>
    /// When the flag is set, all entry cluster are contiguous, so there is no need to read FAT
    /// </summary>
    NoFatChain = 0x02,
}