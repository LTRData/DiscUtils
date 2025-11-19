// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using DiscUtils.ExFat.Internal.Partition;

namespace DiscUtils.ExFat.Internal;
/// <summary>
/// Partition management flags
/// </summary>
[Flags]
public enum ExFatOptions
{
    // ------------ partition flags ---------------

    /// <summary>
    /// Allows <see cref="ExFatPartition"/> to delay some writes (such as cluster chain update=
    /// This is disabled by default (in Default member below)
    /// </summary>
    DelayWrite = 0x0001,

    // ------------ file system flags -------------

    /// <summary>
    /// When set, all reads update the last access time
    /// </summary>
    UpdateLastAccessTime = 0x0100,

    /// <summary>
    /// Default value
    /// </summary>
    Default = UpdateLastAccessTime,
}