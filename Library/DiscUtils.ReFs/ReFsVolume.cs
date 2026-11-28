using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using DiscUtils.ReFs.BTree;
using DiscUtils.ReFs.BTree.Editing;
using DiscUtils.ReFs.Checksums;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Compression;
using DiscUtils.ReFs.Diagnostics;
using DiscUtils.ReFs.Diagnostics.Enums;
using DiscUtils.ReFs.Entries;
using DiscUtils.ReFs.Entries.Directories;
using DiscUtils.ReFs.Entries.Files;
using DiscUtils.ReFs.Entries.Streams;
using DiscUtils.ReFs.IO;
using DiscUtils.ReFs.Mount;
using DiscUtils.ReFs.Mount.Enums;
using DiscUtils.ReFs.Replay;
using DiscUtils.ReFs.SystemTables;

namespace DiscUtils.ReFs;

/// <summary>
/// Read-only view of a mounted ReFS volume.
/// </summary>
internal sealed class ReFsVolume : IStreamOpener
{
    /// <summary>The only on-disk family this reader understands (3.1 to 3.14 seen); ReFS 1.x is laid out differently.</summary>
    private const byte SupportedMajorVersion = 3;

    private static readonly char[] PathSeparators = ['\\', '/'];

    private readonly FileReader _files;
    private readonly TreeReader _tree;
    private readonly ObjectTable _objects;
    private readonly IBlockSource _volume;
    private readonly Checkpoint _checkpoint;
    private readonly Lazy<SecurityTable> _security;
    private readonly Lazy<UpcaseTable> _upcase;
    private readonly Lazy<VolumeInformation> _information;
    private readonly Lazy<ReFsEntry> _root;
    private readonly Lazy<AllocatorSummary> _allocatorSummary;

    private ReFsVolume(
        IBlockSource volume,
        VolumeInfoDto info,
        TreeReader tree,
        ObjectTable objects,
        ContainerMap containers,
        StreamChecksumPolicy? streamChecksums,
        bool verifyData,
        Checkpoint checkpoint,
        bool replayLog,
        int? replayTransactions)
    {
        Info = info;
        Containers = containers;
        _tree = tree;
        _objects = objects;
        _volume = volume;
        _checkpoint = checkpoint;
        ReplayEnabled = replayLog;
        VerifyDataChecksums = verifyData;
        Overlay = replayLog ? BuildOverlay(replayTransactions) : RedoOverlay.Empty;
        _security = new Lazy<SecurityTable>(() => new SecurityTable(tree, objects, Overlay));
        _upcase = new Lazy<UpcaseTable>(() => UpcaseTable.Load(tree, objects, NullLogger.Instance));
        _information = new Lazy<VolumeInformation>(() => VolumeInformation.Load(tree, objects, NullLogger.Instance));
        _allocatorSummary = new Lazy<AllocatorSummary>(() => AllocatorSummary.Load(tree, checkpoint));
        Directories = new DirectoryReader(tree.Pages, tree, objects, _upcase, Overlay, NullLogger.Instance);
        _files = new FileReader(volume, tree.Pages, tree, streamChecksums, verifyData, NullLogger.Instance, _upcase);
        _root = new Lazy<ReFsEntry>(() => Directories.GetDirectoryEntry(string.Empty, DirectoryReader.RootObjectId));
    }

    public VolumeInfoDto Info { get; }

    /// <summary>Size of the volume in bytes: the boot sector's cluster count times the cluster size.</summary>
    public long Size => checked((long)(Info.TotalClusters * Info.ClusterSize));

    /// <summary>
    /// Bytes in use: the volume's clusters minus the free clusters of the volume allocator's summary (<see cref="AllocatorSummary" />). Clusters the
    /// allocator does not cover count as used. Read on first use from the mounted checkpoint, so on a volume with an unreplayed redo log it can be
    /// slightly behind what Windows reports after its own replay. Compressed containers count their stored (packed) clusters.
    /// </summary>
    /// <exception cref="ReFsFormatException">The allocator summary is missing or malformed, or reports more free clusters than the volume has.</exception>
    public long UsedSpace => checked((long)((Info.TotalClusters - CheckedAllocatorSummary().Free) * Info.ClusterSize));

    /// <summary>
    /// Bytes free: the free clusters of the volume allocator's summary minus its global stream reserve. Windows also subtracts the driver's runtime
    /// reserve ("Total Reserved" in <c>fsutil fsinfo refsinfo</c>), which is not stored on the volume, so this can exceed Windows' free figure by that
    /// amount. Read from the mounted checkpoint, as <see cref="UsedSpace" />.
    /// </summary>
    /// <exception cref="ReFsFormatException">The allocator summary is missing or malformed, or reports more free clusters than the volume has.</exception>
    public long AvailableSpace
    {
        get
        {
            var summary = CheckedAllocatorSummary();
            var free = summary.Free > summary.GlobalStreamReserve ? summary.Free - summary.GlobalStreamReserve : 0;
            return checked((long)(free * Info.ClusterSize));
        }
    }

    /// <summary>The volume allocator's summary (checkpoint root 1). Read on first use.</summary>
    internal AllocatorSummary AllocatorSummary => _allocatorSummary.Value;

    /// <summary>The summary, checked against the volume's cluster count (its free clusters must fit in the volume).</summary>
    private AllocatorSummary CheckedAllocatorSummary()
    {
        var summary = AllocatorSummary;
        if (summary.Free > Info.TotalClusters)
        {
            throw new ReFsFormatException($"The allocator summary reports 0x{summary.Free:X} free clusters on a volume of 0x{Info.TotalClusters:X}.");
        }

        return summary;
    }

    /// <summary>Whether the committed redo-log records after the checkpoint were applied in memory (experimental).</summary>
    public bool ReplayEnabled { get; }

    internal TreeReader Tree => _tree;

    internal Checkpoint Checkpoint => _checkpoint;

    internal ObjectTable Objects => _objects;

    internal SecurityTable Security => _security.Value;

    internal bool VerifyDataChecksums { get; }

    internal ILogger Logger => NullLogger.Instance;

    /// <summary>Checks a file's stream-set control rows against its stored tables; for validation.</summary>
    internal void CheckStreamSets(ReFsEntry file)
    {
        if (file.Location?.IncompleteReason is not null)
        {
            return;
        }

        new FileRecord(_tree, (file.Location ?? throw new InvalidOperationException("The entry has no on-disk location.")).ReadRow(_tree.Pages).Value).CheckStreamSets();
    }

    /// <summary>Rows changed by the replayed redo log (empty unless replay was requested).</summary>
    internal RedoOverlay Overlay { get; }

    public ReFsEntry Root => _root.Value;

    /// <summary>The container table: where container-space clusters lie on the volume, and which containers are compressed.</summary>
    internal ContainerMap Containers { get; }

    internal DirectoryReader Directories { get; }

    /// <summary>The volume label, or an empty string when the volume has no volume information table.</summary>
    public string Label
    {
        get
        {
            try
            {
                return _information.Value.Label;
            }
            catch (Exception ex) when (ex is ReFsFormatException or IOException or NotSupportedException)
            {
                return string.Empty;
            }
        }
    }

    /// <summary>Reads the redo log from the checkpoint's log start to the end of the valid log without applying it; null when the volume has no log.</summary>
    internal RedoLogScan? ReadRedoLog()
    {
        if (LogLocation.Load(_tree, _objects, NullLogger.Instance) is not { } location)
        {
            return null;
        }

        return RedoLogScan.Read(_volume, Info.ClusterSize, location, _checkpoint.LogStartLsn);
    }

    private RedoOverlay BuildOverlay(int? transactions)
    {
        var scan = ReadRedoLog();

        if (scan is null || scan.Entries == 0)
        {
            return RedoOverlay.Empty;
        }

        return RedoOverlay.Build(NullLogger.Instance, scan, _objects, ReadStoredRow, ReadBranchRows, transactions, _checkpoint.RestartClock);
    }

    /// <summary>Every row of an embedded table whose node is a branch (the rows sit in metadata pages of their own).</summary>
    private List<EmbeddedRow> ReadBranchRows(ReadOnlySpan<byte> value, int nodeOffset)
    {
        var rows = new List<EmbeddedRow>();
        foreach (var entry in _tree.Rows(value, nodeOffset))
        {
            rows.Add(new EmbeddedRow(entry.Flags, entry.Key.ToArray(), entry.Value.ToArray()));
        }

        return rows;
    }

    /// <summary>Reads a row of an on-disk table by exact key with a full scan (used to patch rows the log updates in place).</summary>
    private byte[]? ReadStoredRow(ulong objectId, byte[] key)
    {
        if (!_objects.TryGetRoot(objectId, out var root))
        {
            return null;
        }

        foreach (var entry in _tree.Rows(root))
        {
            if (entry.Key.SequenceEqual(key))
            {
                return entry.Value.ToArray();
            }
        }

        return null;
    }

    /// <summary>Mounts the volume found at the start of <paramref name="stream" />. The stream is not owned.</summary>
    /// <exception cref="InvalidFileSystemException">The volume is damaged or is not a ReFS volume.</exception>
    /// <exception cref="NotSupportedException">The volume uses a ReFS version this reader does not support.</exception>
    public static ReFsVolume Open(Stream stream, bool verifyChecksums = true, bool verifyDataChecksums = true, bool replayLog = false, int? replayTransactions = null)
    {
        // Damaged or non-ReFS volumes raise ReFsFormatException, which is an InvalidFileSystemException.
        return Mount(new StreamBlockSource(stream), verifyChecksums, verifyDataChecksums, replayLog, replayTransactions);
    }

    public IEnumerable<ReFsEntry> Enumerate(ReFsEntry directory)
    {
        if (directory.IsDirectory)
        {
            return Directories.Enumerate(directory.ObjectId);
        }

        throw new IOException($"'{directory.Name}' is not a directory.");
    }

    /// <summary>Gets a file or directory by path (case-insensitive; '\' or '/' separators).</summary>
    public bool TryGetEntry(string path, [NotNullWhen(true)] out ReFsEntry? entry)
    {
        var current = _root.Value;

        foreach (var component in path.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!current.IsDirectory || FindChild(current, component) is not { } child)
            {
                entry = null;
                return false;
            }

            current = child;
        }

        entry = current;
        return true;
    }

    public ReFsEntry? FindChild(ReFsEntry directory, string name)
    {
        // Keyed B+tree search first; a miss falls through to a full scan so an unusual directory ordering
        // can only cost time, never produce a false "not found".
        if (Directories.TryFind(directory.ObjectId, name, out var hit))
        {
            return hit;
        }

        return Directories.FindByScan(directory.ObjectId, name);
    }

    /// <summary>
    /// Gets the entry's security descriptor as raw self-relative bytes. False when the entry has none or its id is not in the security table.
    /// </summary>
    public bool TryGetSecurityDescriptor(ReFsEntry entry, out ReadOnlyMemory<byte> descriptor)
    {
        var securityId = entry.IsDirectory ? Directories.GetDirectorySecurityId(entry.ObjectId) : entry.SecurityId;
        return _security.Value.TryGet(securityId, out descriptor);
    }

    /// <summary>The number of names the file has. Only a hard-linked file's backing record holds the real count.</summary>
    public int GetLinkCount(ReFsEntry entry)
    {
        if (entry.IsDirectory || entry.HolderDirectory == 0 || entry.Location is null)
        {
            return 1;
        }

        var record = new FileRecord(_tree, entry.Location.Value.ReadValue(_tree.Pages));
        return (int)Math.Max(1u, record.Info.LinkCount);
    }

    /// <summary>Extended attributes in on-disk order (empty when there are none).</summary>
    public IReadOnlyList<ReFsExtendedAttributeEntry> GetExtendedAttributes(ReFsEntry entry)
    {
        if (entry.Location?.IncompleteReason is { } reason)
        {
            throw new NotSupportedException($"The extended attributes of this file cannot be read: redo-log replay did not apply every change to it ({reason}).");
        }

        var record = entry.IsDirectory
            ? Directories.ReadOwnRowValue(entry.ObjectId)
            : (entry.Location ?? throw new InvalidOperationException("The entry has no on-disk location.")).ReadRow(_tree.Pages).Value;

        return ReFsExtendedAttributeEntry.Read(_tree, record);
    }

    /// <summary>
    /// Walks supported checkpoint roots, object trees, the upcase table, security row envelopes and directory entries, including
    /// extended attributes and security-id references. Unless <paramref name="verifyFileData"/> is false, also reads file streams.
    /// </summary>
    /// <remarks>File data is read through <paramref name="opener"/> (this volume when null), so a deduplicated file can be checked against its chunk store.</remarks>
    public Validation.ValidationReport Validate(bool verifyFileData = true, IProgress<Validation.ValidationProgress>? progress = null, IStreamOpener? opener = null,
        CancellationToken cancellationToken = default)
        => Validation.VolumeValidator.Run(this, verifyFileData, progress, opener, cancellationToken);

    /// <summary>
    /// Opens a data stream of a file as stored: the default stream, or the named (alternate) stream. A file optimized by Data Deduplication
    /// (reparse tag 0x80000013) reads as its sparse stub; <see cref="ReFsDedupReader"/> reads it as Windows does. A non-resident stream
    /// reports its runs as <see cref="DiscUtils.Streams.SparseStream.Extents"/>.
    /// </summary>
    public Stream OpenRead(ReFsEntry entry, string? stream = null) => _files.Open(entry, stream);

    /// <summary>
    /// Reads every stream of a file in full (the default stream, named streams and snapshots, as stored) and reports how much of the data was
    /// checked against checksums. A checksum mismatch throws, as any read does. Data checksums are checked only when the volume verifies them.
    /// </summary>
    public ReFsDataVerification VerifyData(ReFsEntry entry, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        if (entry.IsDirectory)
        {
            throw new IOException($"'{entry.Name}' is a directory.");
        }

        var buffer = new byte[1 << 20];
        ulong bytes = 0;
        ulong verified = 0;
        ulong unwritten = 0;
        var kinds = new HashSet<ChecksumKind>();

        foreach (var info in entry.Streams)
        {
            using var stream = _files.Open(entry, info.Name.Length == 0 ? null : info.Name);
            ulong read = 0;
            int n;

            while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                read += (ulong)n;
                progress?.Report(n);
            }

            bytes += read;
            if (stream is RunFileStream run)
            {
                // A last checksum block counts in full even when the stream ends inside it, so the checked bytes are capped at the written ones.
                var never = Math.Min(read, (ulong)run.UnwrittenBytes);
                unwritten += never;
                verified += Math.Min(read - never, (ulong)run.VerifiedBytes);
                kinds.UnionWith(run.VerifiedKinds);
            }
        }

        return new ReFsDataVerification(bytes, verified, unwritten, kinds.OrderBy(k => k).Select(k => (ReFsChecksumKind)k).ToList());
    }

    /// <summary>Volume-wide details for tools.</summary>
    public ReFsVolumeDetails GetDetails()
    {
        var info = Info;
        var information = _information.Value;
        return new ReFsVolumeDetails(
            info.FormatVersion,
            info.BootSectorVersion,
            info.SecondaryCheckpointVersion,
            info.ClusterSize,
            info.TotalClusters,
            info.Serial,
            info.VolumeId,
            (ReFsChecksumKind)info.MetadataChecksum,
            (ReFsChecksumKind)info.DataChecksum,
            info.DataChecksumBlockSize,
            information.Label,
            information.Created,
            information.Mounted,
            information.DriverVersion,
            information.VolumeVersion,
            (uint)information.Flags,
            info.CheckpointClock,
            _checkpoint.RestartClock,
            (uint)info.CheckpointFeatures,
            FlagNames(info.CheckpointFeatures),
            (info.CheckpointFeatures & CheckpointFeatures.DeveloperVolume) != 0,
            ReplayEnabled);
    }

    /// <summary>The names of the known bits and the rest as hex.</summary>
    private static string FlagNames(CheckpointFeatures features)
    {
        var names = new List<string>();
        uint known = 0;
        foreach (CheckpointFeatures flag in Enum.GetValues(typeof(CheckpointFeatures)))
        {
            if (flag != CheckpointFeatures.None && (features & flag) == flag)
            {
                names.Add(flag.ToString());
                known |= (uint)flag;
            }
        }

        var unknown = (uint)features & ~known;
        if (unknown != 0)
        {
            names.Add($"0x{unknown:X}");
        }

        return names.Count == 0 ? "None" : string.Join(", ", names);
    }

    /// <summary>
    /// The data of the reparse point of <paramref name="entry"/> as stored (after its tag and length), or null when the entry has none. Unlike
    /// <see cref="ReFsEntry.Reparse"/>, this returns the data of any tag, such as Data Deduplication's.
    /// </summary>
    public byte[]? GetReparseData(ReFsEntry entry)
    {
        if (entry.Reparse is null)
        {
            return null;
        }

        var location = entry.Location ?? throw new InvalidOperationException("The entry was not produced by a volume enumeration.");
        return new FileRecord(_tree, location.ReadValue(_tree.Pages)).GetReparsePointRow()?.Data
               ?? throw new ReFsFormatException($"'{entry.Name}' is listed with a reparse point that cannot be read.");
    }

    private static ReFsVolume Mount(IBlockSource volume, bool verifyChecksums, bool verifyData, bool replayLog, int? replayTransactions)
    {
        var boot = ReadBootSector(volume);

        if (boot.TotalSectors > (ulong)(volume.Length / boot.BytesPerSector))
        {
            throw new ReFsFormatException("The advertised ReFS volume size exceeds the stream.");
        }

        // ReFS 1.x (Server 2012 and 2012 R2) has another on-disk layout.
        if (boot.MajorVersion != SupportedMajorVersion)
        {
            throw new NotSupportedException($"ReFS {boot.MajorVersion}.{boot.MinorVersion} is not supported: only ReFS {SupportedMajorVersion}.x volumes are read.");
        }

        var clusterSize = boot.ClusterSize;

        if (boot.TotalClusters <= SuperBlock.PrimaryLcn + 3)
        {
            throw new ReFsFormatException("Volume too small.");
        }

        var superBlock = ReadSuperBlock(volume, clusterSize, boot.TotalClusters);
        var checkpoint = ReadNewestCheckpoint(volume, superBlock.CheckpointLcns, clusterSize)
            ?? throw new ReFsFormatException("No valid checkpoint found.");
        checkpoint.ValidateVersion(boot.MajorVersion, boot.MinorVersion);

        var pages = new PageReader(volume, clusterSize, verifyChecksums, 512, NullLogger.Instance);
        var tree = new TreeReader(pages)
        {
            StandardInformationMinimumLength = StandardInformation.MinimumLengthFor(new Version(boot.MajorVersion, boot.MinorVersion))
        };
        var containers = BuildContainerMap(tree, checkpoint);
        pages.Map = containers;
        containers.Compression = new CompressionContext(volume, clusterSize, ManagedChunkDecompressor.Instance, verifyData, new CompressionTableReader(tree, checkpoint.Roots));

        var firstRoot = checkpoint.Roots[Checkpoint.ObjectTableRoot];
        var streamChecksums = StreamChecksumPolicy.Derive(firstRoot.Kind, clusterSize);

        var info = new VolumeInfoDto(
            new Version(boot.MajorVersion, boot.MinorVersion), clusterSize, boot.TotalClusters, boot.VolumeSerial,
            superBlock.VolumeId, checkpoint.Clock, !checkpoint.WrittenAtCleanShutdown, checkpoint.Features,
            new Version(boot.MajorVersion, boot.MinorVersion),
            new Version(checkpoint.SecondaryMajorVersion, checkpoint.SecondaryMinorVersion), firstRoot.Kind,
            streamChecksums?.Kind ?? ChecksumKind.None, streamChecksums?.BlockSize ?? 0);

        var objects = new ObjectTable(tree, checkpoint.Roots[Checkpoint.ObjectTableRoot]);
        return new ReFsVolume(volume, info, tree, objects, containers, streamChecksums, verifyData, checkpoint, replayLog, replayTransactions);
    }

    /// <summary>
    /// Reads the three superblock copies (cluster 30, total - 2 and total - 3), each validated on its own, and picks one as Windows does: a
    /// valid primary (cluster 30) is chosen whatever the generation of the backups. Only without a valid primary does the generation decide
    /// between the two backups, the earlier one winning a tie. No valid copy fails the mount. Windows then rewrites stale or invalid copies;
    /// this reader does not write.
    /// </summary>
    private static SuperBlock ReadSuperBlock(IBlockSource volume, uint clusterSize, ulong totalClusters)
    {
        SuperBlock? chosen = null;
        ulong chosenLcn = 0;
        string? firstProblem = null;

        foreach (var lcn in new[] { SuperBlock.PrimaryLcn, totalClusters - 2, totalClusters - 3 })
        {
            try
            {
                var copy = new SuperBlock(volume.ReadBytes(BlockSourceExtensions.ClusterOffset(lcn, clusterSize), (int)clusterSize), lcn, totalClusters);
                if (chosen is null || (chosenLcn != SuperBlock.PrimaryLcn && copy.Generation > chosen.Generation))
                {
                    chosen = copy;
                    chosenLcn = lcn;
                }
            }
            catch (Exception ex) when (ex is ReFsFormatException or IOException)
            {
                firstProblem ??= ex.Message;
            }
        }

        return chosen ?? throw new ReFsFormatException($"No valid superblock copy found (cluster {SuperBlock.PrimaryLcn}: {firstProblem}).");
    }

    /// <summary>The roots whose blocks Windows checks before it accepts a checkpoint (12, 7 and 8, all addressed physically).</summary>
    private static readonly int[] CheckedCheckpointRoots = [Checkpoint.PhysicalAllocatorRoot, Checkpoint.ContainerTableRoot, Checkpoint.ContainerTableMirrorRoot];

    /// <summary>
    /// Reads the checkpoint copies and returns the one Windows mounts, or null. Windows makes two passes: on the first it also reads the
    /// blocks of roots 12, 7 and 8 and checks each against the checksum in its root pointer; only when no copy passes does the second pass
    /// accept copies without that check. Within a pass the highest clock wins.
    /// </summary>
    private static Checkpoint? ReadNewestCheckpoint(IBlockSource volume, IEnumerable<ulong> checkpointLcns, uint clusterSize)
    {
        Checkpoint? best = null;
        Checkpoint? bestWithGoodRoots = null;

        foreach (var lcn in checkpointLcns)
        {
            try
            {
                var candidate = new Checkpoint(volume.ReadBytes(BlockSourceExtensions.ClusterOffset(lcn, clusterSize), (int)clusterSize), lcn);
                if (best is null || candidate.Clock > best.Clock)
                {
                    best = candidate;
                }

                if (FirstBadRoot(volume, candidate, clusterSize) is null && (bestWithGoodRoots is null || candidate.Clock > bestWithGoodRoots.Clock))
                {
                    bestWithGoodRoots = candidate;
                }
            }
            catch (Exception ex) when (ex is ReFsFormatException or IOException)
            {
                // The other copy may still be valid.
            }
        }

        return bestWithGoodRoots ?? best;
    }

    /// <summary>Describes the first of roots 12, 7 and 8 whose block cannot be read or does not match its root pointer's checksum; null when all pass.</summary>
    private static string? FirstBadRoot(IBlockSource volume, Checkpoint checkpoint, uint clusterSize)
    {
        foreach (var index in CheckedCheckpointRoots)
        {
            if (index >= checkpoint.Roots.Length)
            {
                continue;
            }

            var root = checkpoint.Roots[index];
            try
            {
                var data = new byte[root.ClusterCount * (int)clusterSize];
                for (var i = 0; i < root.ClusterCount; i++)
                {
                    volume.ReadExactly(BlockSourceExtensions.ClusterOffset(root.GetLcn(i), clusterSize), data.AsSpan(i * (int)clusterSize, (int)clusterSize));
                }

                if (!root.Verify(data))
                {
                    return $"root {index} (cluster 0x{root.Lcn0:X}) does not match its checksum";
                }
            }
            catch (Exception ex) when (ex is ReFsFormatException or IOException or ArgumentException)
            {
                return $"root {index} (cluster 0x{root.Lcn0:X}) cannot be read: {ex.Message}";
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the container map from the container table (root 7), or from its mirror (root 8) when root 7 cannot be read, as Windows does
    /// (it then rewrites the bad copy, which this reader does not). Root 7 wins when both are readable.
    /// </summary>
    private static ContainerMap BuildContainerMap(TreeReader tree, Checkpoint checkpoint)
    {
        try
        {
            return ContainerMap.Build(tree, checkpoint.Roots[Checkpoint.ContainerTableRoot], NullLogger.Instance);
        }
        catch (Exception primary) when (primary is ReFsFormatException or IOException && checkpoint.Roots.Length > Checkpoint.ContainerTableMirrorRoot)
        {
            try
            {
                return ContainerMap.Build(tree, checkpoint.Roots[Checkpoint.ContainerTableMirrorRoot], NullLogger.Instance);
            }
            catch (Exception ex) when (ex is ReFsFormatException or IOException)
            {
                // Both fail: report root 7's problem, the table the volume normally uses.
                ExceptionDispatchInfo.Capture(primary).Throw();
                throw;
            }
        }
    }

    /// <summary>
    /// Reads the boot sector as Windows does: sector 0, and only when that read fails with an I/O error, the backup in the last sector of
    /// the volume. The sector size is not known here, so the last 512 and the last 4096 bytes are tried, and a copy is kept only when its
    /// own bytes-per-sector matches where it was found. An invalid primary still fails the mount.
    /// </summary>
    private static BootSector ReadBootSector(IBlockSource volume)
    {
        var bootBytes = new byte[(int)Math.Min(4096, volume.Length)];
        try
        {
            volume.ReadExactly(0, bootBytes);
        }
        catch (IOException)
        {
            foreach (var sectorSize in new uint[] { 512, 4096 })
            {
                if (volume.Length < 2L * sectorSize)
                {
                    continue;
                }

                try
                {
                    var backup = new BootSector(volume.ReadBytes(volume.Length - sectorSize, (int)sectorSize));
                    if (backup.BytesPerSector == sectorSize)
                    {
                        return backup;
                    }
                }
                catch (Exception ex) when (ex is ReFsFormatException or IOException)
                {
                    // Not a backup boot sector at this sector size.
                }
            }

            throw;
        }

        return new BootSector(bootBytes);
    }
}