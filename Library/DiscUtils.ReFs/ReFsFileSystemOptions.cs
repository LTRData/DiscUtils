namespace DiscUtils.ReFs;

/// <summary>Options for opening a ReFS volume.</summary>
public sealed class ReFsFileSystemOptions
{
    /// <summary>Verify metadata page checksums on every read (default: true).</summary>
    public bool VerifyChecksums { get; set; } = true;

    /// <summary>Verify the per-block checksums of integrity-stream files and compressed chunks while reading data (default: true).</summary>
    public bool VerifyDataChecksums { get; set; } = true;

    /// <summary>
    /// EXPERIMENTAL, off by default. Applies the committed redo-log records written after the last checkpoint in memory, so a volume
    /// that was not cleanly dismounted shows its latest directory changes. Nothing is ever written to the stream. Files whose data
    /// allocation was changed in the log cannot be read and fail with <see cref="System.NotSupportedException"/>. Opening fails with
    /// <see cref="System.NotSupportedException"/> when the log holds operations that cannot be applied.
    /// </summary>
    public bool ReplayLog { get; set; }

    /// <summary>
    /// With <see cref="ReplayLog"/>: apply only the first this many committed transactions in log order; null applies all of them.
    /// </summary>
    public int? ReplayTransactions { get; set; }

    /// <summary>
    /// Read a file optimized by the Windows Data Deduplication role (reparse tag 0x80000013) as Windows does, from the volume's chunk store under
    /// <c>System Volume Information\Dedup</c> (default: true). When false, such a file reads as its sparse stub, holding only the ranges written
    /// since the optimization. Chunks are checked against their hashes when <see cref="VerifyDataChecksums"/> is set.
    /// </summary>
    public bool ReadDeduplicatedFiles { get; set; } = true;
}
