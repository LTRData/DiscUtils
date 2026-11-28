namespace DiscUtils.ReFs.Entries.Enums;

/// <summary>How an entry relates to redo-log replay (see <see cref="ReFsFileSystemOptions.ReplayLog" />).</summary>
public enum ReFsReplayState
{
    /// <summary>The entry comes from the last checkpoint unchanged.</summary>
    None = 0,

    /// <summary>The entry was created or changed by replayed log records and all of its changes were applied.</summary>
    Replayed = 1,

    /// <summary>
    ///     The entry's name, attributes, times and size were replayed, but log records that change its data streams or extended
    ///     attributes were not applied: reading its data or extended attributes throws <see cref="NotSupportedException" />.
    /// </summary>
    Partial = 2
}