// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;

namespace DiscUtils.ExFat.Internal.Partition.Entries;
/// <summary>
/// File attributes. Here are the only attributes supported by exFAT
/// </summary>
[Flags]
public enum ExFatFileAttributes : ushort
{
    /// <summary>
    /// Read-only file
    /// </summary>
    ReadOnly = 0x01,
    /// <summary>
    /// Hidden entry
    /// </summary>
    Hidden = 0x02,
    /// <summary>
    /// System entry
    /// </summary>
    System = 0x04,
    /// <summary>
    /// Marks a directory (otherwise the entry is a file)
    /// </summary>
    Directory = 0x10,
    /// <summary>
    /// Marks a file for archive (indicates it was modified)
    /// </summary>
    Archive = 0x20,
}