// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using System.Diagnostics.CodeAnalysis;

namespace DiscUtils.ExFat.Internal.Partition.Entries;
/// <summary>
/// Flags for directory entry type
/// </summary>
[Flags]
[SuppressMessage("Design", "CA1069:Enums values should not be duplicated", Justification = "Dual meaning of some flags")]
public enum ExFatDirectoryEntryType : byte
{
    // values

    /// <summary>
    /// Entry is allocation bitmap (relates to <see cref="AllocationBitmapExFatDirectoryEntry"/>)
    /// </summary>
    AllocationBitmap = 0x01,
    /// <summary>
    /// Entry is up-case table (indicates a <see cref="UpCaseTableExFatDirectoryEntry"/>)
    /// </summary>
    UpCaseTable = 0x02,
    /// <summary>
    /// Entry is the volume label (<see cref="VolumeLabelExFatDirectoryEntry"/>)
    /// </summary>
    VolumeLabel = 0x03,
    /// <summary>
    /// Entry is a file (<see cref="FileExFatDirectoryEntry"/>)
    /// </summary>
    File = 0x05,

    /// <summary>
    /// A stream secondary entry (<see cref="StreamExtensionExFatDirectoryEntry"/>)
    /// </summary>
    Stream = Secondary,
    /// <summary>
    /// A file name part secondary entry (<see cref="FileNameExtensionExFatDirectoryEntry"/>)
    /// </summary>
    FileName = Secondary | 0x01,

    // flags

    /// <summary>
    /// When this flag is set, the entry is in use. If not, the slot is available for overwrite
    /// </summary>
    InUse = 0x80,
    /// <summary>
    /// Indicates a secondary entry (stream or file name)
    /// </summary>
    Secondary = 0x40,
    /// <summary>
    /// Indicates a benign entry (guid or a few things I don't remember)
    /// </summary>
    Benign = 0x20,
}