namespace DiscUtils.ReFs.Replay.Enums;

/// <summary>
///     The flags of the log's control page (the u32 at +0x3C). Windows also derives bits from its open flags (0x4, 0x10); these were not seen on
///     the test images and are not named.
/// </summary>
[Flags]
internal enum RedoLogFlags : uint
{
    None = 0,

    /// <summary>Every log page carries a checksum.</summary>
    ChecksumsEnabled = 1,

    /// <summary>
    ///     Meaning unknown: Windows sets it when it opens the log (inferred: when no checksum or transform callbacks are registered). It decides which
    ///     control copy is used: copy A is kept when A has it, and otherwise copy B is taken when B has it or a higher write counter.
    /// </summary>
    Bit0x2 = 2
}