using DiscUtils.Core.WindowsSecurity.AccessControl;
using DiscUtils.ReFs.Diagnostics;
using DiscUtils.ReFs.Entries.Enums;
using DiscUtils.Vfs;

namespace DiscUtils.ReFs;

/// <summary>
/// Read-only access to ReFS 3.x volumes: files, directories, alternate data streams, security descriptors, reparse points,
/// extended attributes, checksum verification, volume compression (LZ4 and Zstandard) and, optionally, in-memory redo-log replay.
/// </summary>
public sealed class ReFsFileSystem : VfsFileSystemFacade, IWindowsFileSystem, DeduplicationRole.IDedupAwareFileSystem
{
    /// <summary>
    /// Initializes a new instance of the ReFsFileSystem class.
    /// </summary>
    /// <param name="stream">A stream starting at the ReFS volume (not a whole partitioned disk).</param>
    public ReFsFileSystem(Stream stream) : base(new VfsReFsFileSystem(stream, null)) { }

    /// <summary>
    /// Initializes a new instance of the ReFsFileSystem class.
    /// </summary>
    /// <param name="stream">A stream starting at the ReFS volume (not a whole partitioned disk).</param>
    /// <param name="parameters">The generic file system parameters (not used by ReFS).</param>
    public ReFsFileSystem(Stream stream, FileSystemParameters? parameters) : base(new VfsReFsFileSystem(stream, null)) { }

    /// <summary>
    /// Initializes a new instance of the ReFsFileSystem class.
    /// </summary>
    /// <param name="stream">A stream starting at the ReFS volume (not a whole partitioned disk).</param>
    /// <param name="options">ReFS specific options, such as checksum verification and redo-log replay.</param>
    public ReFsFileSystem(Stream stream, ReFsFileSystemOptions options) : base(new VfsReFsFileSystem(stream, options)) { }

    // The facade forwards Size, UsedSpace and AvailableSpace but not this flag.
    public override bool SupportsUsedAvailableSpace => Real.SupportsUsedAvailableSpace;

    /// <summary>The ReFS format version of the volume, for example 3.14.</summary>
    public Version FormatVersion => Real.Volume.Info.FormatVersion;

    /// <summary>True when the redo log was replayed in memory (see <see cref="ReFsFileSystemOptions.ReplayLog"/>).</summary>
    public bool ReplayEnabled => Real.Volume.ReplayEnabled;

    private VfsReFsFileSystem Real => GetRealFileSystem<VfsReFsFileSystem>();

    /// <inheritdoc cref="DeduplicationRole.IDedupAwareFileSystem.ReadsDeduplicatedFiles"/>
    bool DeduplicationRole.IDedupAwareFileSystem.ReadsDeduplicatedFiles => Real.ReadsDeduplicatedFiles;

    /// <summary>The parsed volume; for tests.</summary>
    internal ReFsVolume Volume => Real.Volume;

    /// <summary>
    /// Gets the names of the alternate data streams of a file or directory.
    /// </summary>
    /// <param name="path">The path to the file or directory.</param>
    /// <returns>The stream names, excluding the default stream (empty if none). Open one with
    /// <c>OpenFile(path + ":" + name, ...)</c>.</returns>
    public IEnumerable<string> GetAlternateDataStreams(string path) => Real.GetAlternateDataStreams(path);

    /// <summary>
    /// Checks the volume structures (checkpoint roots, object trees, upcase and security tables, directories and their entries) and, unless
    /// <paramref name="verifyFileData"/> is false, reads every file stream including its integrity checks. Problems are collected in the report;
    /// this is not an exhaustive validation of every on-disk structure.
    /// </summary>
    public Validation.ValidationReport Validate(bool verifyFileData = true, IProgress<Validation.ValidationProgress>? progress = null, CancellationToken cancellationToken = default)
        => Real.Validate(verifyFileData, progress, cancellationToken);

    /// <summary>
    /// How a file or directory relates to the replayed redo log: unchanged since the last checkpoint, created or changed by replayed
    /// records, or only partly replayed (its data and extended attributes cannot be read). Always <see cref="ReFsReplayState.None"/>
    /// unless <see cref="ReFsFileSystemOptions.ReplayLog"/> was requested.
    /// </summary>
    public ReFsReplayState GetReplayState(string path) => Real.GetReplayState(path);

    /// <summary>Volume-wide details: versions, checksums, label, times, checkpoint (experimental; see <see cref="ReFsVolumeDetails"/>).</summary>
    public ReFsVolumeDetails GetVolumeDetails() => Real.GetVolumeDetails();

    /// <summary>ReFS details of a file or directory, with its streams and snapshots (experimental).</summary>
    public ReFsEntryDetails GetEntryDetails(string path) => Real.GetEntryDetails(path);

    /// <summary>
    /// Opens a data stream of a file by name: the default stream (empty name), a named stream or a snapshot listed by
    /// <see cref="GetEntryDetails"/>. <see cref="DiscFileSystem.OpenFile(string, FileMode, FileAccess)"/> with <c>path:name</c> opens named streams only.
    /// </summary>
    public Stream OpenStream(string path, string streamName) => Real.OpenStream(path, streamName);

    /// <summary>
    /// Reads every stream of a file (as stored: a file optimized by Data Deduplication is read as its stub, not from the chunk store) and
    /// reports how much of its data was checked against checksums (experimental). A checksum
    /// mismatch throws <see cref="InvalidFileSystemException"/>. Data checksums are checked only when <see cref="ReFsFileSystemOptions.VerifyDataChecksums"/>
    /// is set. <paramref name="progress"/> receives the bytes of each read on the calling thread.
    /// </summary>
    public ReFsDataVerification VerifyData(string path, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
        => Real.VerifyData(path, progress, cancellationToken);

    /// <summary>
    /// Reads the redo log written after the checkpoint in use without applying it; null when the volume has no log (experimental). To see the
    /// volume with the first n committed transactions applied, open it with <see cref="ReFsFileSystemOptions.ReplayLog"/> and
    /// <see cref="ReFsFileSystemOptions.ReplayTransactions"/>.
    /// </summary>
    public ReFsRedoLog? ReadRedoLog() => Real.ReadRedoLog();

    /// <summary>Gets the extended attributes (EAs) of a file or directory in on-disk order; empty when it has none.</summary>
    public IReadOnlyList<ReFsExtendedAttribute> GetExtendedAttributes(string path) => Real.GetExtendedAttributes(path);

    /// <inheritdoc/>
    public RawSecurityDescriptor? GetSecurity(string path) => Real.GetSecurity(path);

    /// <inheritdoc/>
    public void SetSecurity(string path, RawSecurityDescriptor securityDescriptor) => throw new NotSupportedException();

    /// <inheritdoc/>
    public ReparsePoint? GetReparsePoint(string path) => Real.GetReparsePoint(path);

    /// <inheritdoc/>
    public void SetReparsePoint(string path, ReparsePoint reparsePoint) => throw new NotSupportedException();

    /// <inheritdoc/>
    public void RemoveReparsePoint(string path) => throw new NotSupportedException();

    /// <inheritdoc/>
    public bool HasHardLinks(string path) => Real.HasHardLinks(path);

    /// <inheritdoc/>
    public int GetHardLinkCount(string path) => Real.GetHardLinkCount(path);

    /// <summary>ReFS stores no DOS short names; always <c>null</c>.</summary>
    public string? GetShortName(string path) => Real.GetShortName(path);

    /// <inheritdoc/>
    public void SetShortName(string path, string shortName) => throw new NotSupportedException();

    /// <inheritdoc/>
    public WindowsFileInformation GetFileStandardInformation(string path) => Real.GetFileStandardInformation(path);

    /// <inheritdoc/>
    public void SetFileStandardInformation(string path, WindowsFileInformation info) => throw new NotSupportedException();

    /// <summary>
    /// Gets the object id of a directory or the file number of a file. File numbers repeat between directories.
    /// </summary>
    public long GetFileId(string path) => Real.GetFileId(path);

    internal static bool Detect(Stream stream) => ReFsBootSector.Detect(stream);
}