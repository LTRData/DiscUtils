// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;

namespace DiscUtils.ExFat.Internal.Partition.Entries;
/// <summary>
/// Flags for <see cref="ExFatAllocationBitmap"/>
/// </summary>
[Flags]
public enum AllocationBitmapFlags : byte
{
    /// <summary>
    /// Indicates this bitmap is the second allocation bitmap
    /// </summary>
    SecondClusterBitmap = 0x01,
}