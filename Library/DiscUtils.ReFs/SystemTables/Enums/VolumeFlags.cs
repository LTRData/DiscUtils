namespace DiscUtils.ReFs.SystemTables.Enums;

/// <summary>
/// The flags word of the volume information metadata row. "Confirmed" bits are tied to a
/// documented Windows flag (<c>PERSISTENT_VOLUME_STATE_*</c>); the rest are inferred from observed behaviour of Windows.
/// Bits 0x1, 0x2, 0x80 and 0x100 have no known use; forefst reports 0x180 on
/// volumes formatted before 3.10, which is not confirmed. All 18 test images are native 3.14 and read 0.
/// </summary>
[Flags]
internal enum VolumeFlags : uint
{
    None = 0,

    /// <summary>
    ///     Inferred. Windows clears it at every mount. When the mount finds it set, it opens the root directory
    ///     differently and ends with a checkpoint and a volume flush. Something outside the normal mount path
    ///     (format or a repair tool) must set it.
    /// </summary>
    ClearedAtMount = 0x4,

    /// <summary>
    /// Inferred: it appears to be set and cleared around a change journal deletion, so it marks one that is under way. A
    /// mount that finds it set goes on to run a volume analysis.
    /// </summary>
    UsnJournalDeleteInProgress = 0x8,

    /// <summary>Inferred. The mount clears it right after the volume is marked mounted, so it is a one-shot request set from outside. Meaning unknown.</summary>
    PendingAtMount = 0x10,

    /// <summary>Confirmed: <c>PERSISTENT_VOLUME_STATE_VOLUME_SCRUB_DISABLED</c> (0x2) sets this bit.</summary>
    ScrubDisabled = 0x20,

    /// <summary>
    ///     Meaning unknown. It appears to have an effect on how the root directory is opened only together with <see cref="ClearedAtMount" />.
    /// </summary>
    Bit0x40 = 0x40,

    /// <summary>Confirmed both ways: <c>PERSISTENT_VOLUME_STATE_NO_HEAT_GATHERING</c> (0x10).</summary>
    NoHeatGathering = 0x200,

    /// <summary>Confirmed both ways: <c>PERSISTENT_VOLUME_STATE_NO_WRITE_AUTO_TIERING</c> (0x80).</summary>
    NoWriteAutoTiering = 0x400,

    /// <summary>Inferred. Windows sets it when clear and then updates the security table: the one-time global security descriptor update was done.</summary>
    GlobalSecurityUpdated = 0x800
}