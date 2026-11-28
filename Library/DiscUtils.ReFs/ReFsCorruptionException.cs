using DiscUtils.ReFs.Checksums;

namespace DiscUtils.ReFs;

/// <summary>A metadata page or a block of file data failed checksum verification.</summary>
internal sealed class ReFsCorruptionException : ReFsFormatException
{
    /// <remarks>
    ///     <paramref name="staleView" /> is set when the volume's checkpoint has moved on since the volume was opened: the volume is in use
    ///     and what was read at open time is out of date, so the mismatch need not be damage.
    /// </remarks>
    public ReFsCorruptionException(ulong lcn, ChecksumKind kind, string target = "metadata page", string? staleView = null) : base($"Checksum ({kind}) mismatch for {target} at cluster 0x{lcn:X}.{(staleView is null ? string.Empty : " " + staleView)}")
    {
        Lcn = lcn;
        Kind = kind;
        VolumeChangedSinceOpen = staleView is not null;
    }

    public ReFsCorruptionException(string message, ChecksumKind kind = ChecksumKind.None) : base(message)
    {
        Kind = kind;
    }

    public ulong Lcn { get; }

    public ChecksumKind Kind { get; }

    /// <summary>True when the volume was written after it was opened (see the remarks).</summary>
    public bool VolumeChangedSinceOpen { get; }
}