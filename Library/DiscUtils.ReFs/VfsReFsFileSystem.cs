using DiscUtils.Core.WindowsSecurity.AccessControl;
using DiscUtils.ReFs.Diagnostics;
using DiscUtils.ReFs.Entries;
using DiscUtils.Vfs;

namespace DiscUtils.ReFs;

internal sealed class VfsReFsFileSystem : VfsReadOnlyFileSystem<ReFsDirEntry, ReFsFile, ReFsDirectory, ReFsContext>, IWindowsFileSystem
{
    public VfsReFsFileSystem(Stream stream, ReFsFileSystemOptions? options) : base(new DiscFileSystemOptions())
    {
        options ??= new ReFsFileSystemOptions();

        if (!ReFsBootSector.Detect(stream))
        {
            throw new InvalidFileSystemException("Invalid boot sector - probably not a ReFS file system.");
        }

        Context = new ReFsContext
        {
            RawStream = stream,
            Volume = ReFsVolume.Open(stream, options.VerifyChecksums, options.VerifyDataChecksums, options.ReplayLog, options.ReplayTransactions)
        };

        if (options.ReadDeduplicatedFiles)
        {
            _dedup = new ReFsDedupReader(Context.Volume);
        }

        Context.Opener = (IStreamOpener?)_dedup ?? Context.Volume;
        RootDirectory = new ReFsDirectory(Context, Context.Volume.Root);
    }

    private readonly ReFsDedupReader? _dedup;

    /// <summary>Whether optimized files are read from the chunk store (<see cref="ReFsFileSystemOptions.ReadDeduplicatedFiles"/>).</summary>
    internal bool ReadsDeduplicatedFiles => _dedup is not null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dedup?.Dispose();
        }

        base.Dispose(disposing);
    }

    // Name lookups use the volume's stored upcase table (case-insensitive).
    public override bool IsCaseSensitive => false;

    public override string FriendlyName => "ReFS";

    public override string VolumeLabel => Context.Volume.Label;

    /// <summary>The low 32 bits of the volume serial number.</summary>
    public override uint VolumeId => (uint)Context.Volume.Info.Serial;

    public override long Size => Context.Volume.Size;

    // From the volume allocator's summary as of the mounted checkpoint; see ReFsVolume.UsedSpace and AvailableSpace.
    public override bool SupportsUsedAvailableSpace => true;

    public override long UsedSpace => Context.Volume.UsedSpace;

    public override long AvailableSpace => Context.Volume.AvailableSpace;

    internal ReFsVolume Volume => Context.Volume;

    private ReFsFile Resolve(string path) =>
        GetFile(path) ?? throw new FileNotFoundException("File not found", path);

    /// <summary>Gets the names of the alternate data streams of a file or directory (empty if none).</summary>
    public IEnumerable<string> GetAlternateDataStreams(string path) => Resolve(path).AlternateStreamNames;

    public RawSecurityDescriptor? GetSecurity(string path)
    {
        var file = Resolve(path);

        if (Volume.TryGetSecurityDescriptor(file.Entry, out var descriptor))
        {
            return new RawSecurityDescriptor(descriptor.Span);
        }

        return null;
    }

    public void SetSecurity(string path, RawSecurityDescriptor securityDescriptor) => throw new NotSupportedException();

    public ReparsePoint? GetReparsePoint(string path)
    {
        // A third-party tag's content is its owner GUID followed by the data, as in the on-disk REPARSE_GUID_DATA_BUFFER after the 8-byte header.
        if (Resolve(path).Entry.Reparse is { } reparse)
        {
            var reparseData = reparse.ReparseGuid is { } owner ? [.. owner.ToByteArray(), .. reparse.Data] : reparse.Data;
            return new ReparsePoint(unchecked((int)reparse.Tag), reparseData);
        }

        return null;
    }

    public void SetReparsePoint(string path, ReparsePoint reparsePoint) => throw new NotSupportedException();

    public void RemoveReparsePoint(string path) => throw new NotSupportedException();

    public bool HasHardLinks(string path) => GetHardLinkCount(path) > 1;

    public int GetHardLinkCount(string path) => Volume.GetLinkCount(Resolve(path).Entry);

    /// <summary>ReFS stores no DOS 8.3 short names.</summary>
    public string? GetShortName(string path)
    {
        Resolve(path);
        return null;
    }

    public void SetShortName(string path, string shortName) => throw new NotSupportedException();

    public WindowsFileInformation GetFileStandardInformation(string path)
    {
        var entry = Resolve(path).Entry;

        return new WindowsFileInformation
        {
            CreationTime = entry.Created.UtcDateTime,
            LastWriteTime = entry.Modified.UtcDateTime,
            ChangeTime = entry.Changed.UtcDateTime,
            LastAccessTime = entry.Accessed.UtcDateTime,
            FileAttributes = ReFsFile.ToFileAttributes(entry)
        };
    }

    public void SetFileStandardInformation(string path, WindowsFileInformation info) => throw new NotSupportedException();

    /// <summary>
    /// The object id of a directory or the file number of a file. File numbers are unique only within one directory,
    /// except for hard-linked files, whose number identifies the backing record.
    /// </summary>
    public long GetFileId(string path)
    {
        var entry = Resolve(path).Entry;
        return unchecked((long)(entry.IsDirectory ? entry.ObjectId : entry.FileId));
    }

    internal Validation.ValidationReport Validate(bool verifyFileData, IProgress<Validation.ValidationProgress>? progress, CancellationToken cancellationToken)
        => Volume.Validate(verifyFileData, progress, Context.Opener, cancellationToken);

    internal Entries.Enums.ReFsReplayState GetReplayState(string path) => Resolve(path).Entry.Replay;

    internal ReFsVolumeDetails GetVolumeDetails() => Volume.GetDetails();

    internal ReFsEntryDetails GetEntryDetails(string path)
    {
        var entry = Resolve(path).Entry;
        return new ReFsEntryDetails(
            entry.IsDirectory,
            entry.AllocatedLength,
            entry.ObjectId,
            entry.FileId,
            entry.Attributes.HasFlag(Entries.Enums.ReFsAttributes.IntegrityStream),
            entry.Replay,
            entry.Streams.Select(s => new ReFsStreamDetails(s.Name, s.Length, s.IsResident, s.IsSnapshot, s.SnapshotTime)).ToList());
    }

    internal ReFsDataVerification VerifyData(string path, IProgress<long>? progress, CancellationToken cancellationToken)
        => Volume.VerifyData(Resolve(path).Entry, progress, cancellationToken);

    internal Stream OpenStream(string path, string streamName)
    {
        var entry = Resolve(path).Entry;
        return Context.Opener.OpenRead(entry, streamName.Length == 0 ? null : streamName);
    }

    internal ReFsRedoLog? ReadRedoLog()
    {
        if (Volume.ReadRedoLog() is not { } scan)
        {
            return null;
        }

        var transactions = scan.Transactions.Select(t => new ReFsRedoTransaction(t.Committed, t.Records.Select(r => new ReFsRedoRecord(
            r.EntryLsn,
            (uint)r.Operation,
            r.Operation.ToString(),
            r.Path.Select(c => new ReFsRedoPathComponent(c.Kind, c.Key)).ToList(),
            r.Values,
            r.Clock,
            r.RestartClock)).ToList())).ToList();

        return new ReFsRedoLog((long)(scan.Location.DataClusters * Volume.Info.ClusterSize / 4096), scan.StartLsn, scan.EndLsn, scan.Entries, transactions);
    }

    internal IReadOnlyList<ReFsExtendedAttribute> GetExtendedAttributes(string path)
    {
        return Volume.GetExtendedAttributes(Resolve(path).Entry)
                     .Select(e => new ReFsExtendedAttribute(e.Name, e.IsNeedEa, e.Value.ToArray()))
                     .ToList();
    }

    protected override ReFsFile ConvertDirEntryToFile(ReFsDirEntry dirEntry)
    {
        if (dirEntry.IsDirectory)
        {
            return new ReFsDirectory(Context, dirEntry.Entry);
        }

        return new ReFsFile(Context, dirEntry.Entry);
    }
}