// Logging is not part of the DiscUtils port: calls compile away.

using System.Diagnostics;
using DiscUtils.ReFs.BTree.Enums;
using DiscUtils.ReFs.Checksums;
using DiscUtils.ReFs.Compression.Enums;
using DiscUtils.ReFs.Mount.Enums;
using DiscUtils.ReFs.Replay.Enums;
using DiscUtils.ReFs.SystemTables.Enums;

namespace DiscUtils.ReFs.Compat;

/// <summary>Minimal logger placeholder; the port never logs.</summary>
internal enum LogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    None
}

internal interface ILogger
{
    bool IsEnabled(LogLevel level);
}

internal interface ILogger<T> : ILogger { }

internal sealed class NullLogger : ILogger
{
    public static NullLogger Instance { get; } = new NullLogger();

    public bool IsEnabled(LogLevel level) => false;
}

internal static class Logs
{
    [Conditional("REFS_LOGGING")]
    public static void Opening(ILogger logger, long offset, long length, bool verifyChecksums, bool replayLog) { }

    [Conditional("REFS_LOGGING")]
    public static void BootSector(ILogger logger, int major, int minor, uint clusterSize, ulong totalClusters, ulong serial) { }

    [Conditional("REFS_LOGGING")]
    public static void SuperBlock(ILogger logger, Guid volumeId, string checkpoints) { }

    [Conditional("REFS_LOGGING")]
    public static void CheckpointRead(ILogger logger, ulong lcn, ulong clock, int major, int minor, int roots, CheckpointFeatures features, ulong logStart, int payload) { }

    [Conditional("REFS_LOGGING")]
    public static void CheckpointInvalid(ILogger logger, ulong lcn, string reason) { }

    [Conditional("REFS_LOGGING")]
    public static void Mounted(ILogger logger, Version version, Version bootVersion, uint clusterSize, ulong totalClusters, ulong clock, CheckpointFeatures features) { }

    [Conditional("REFS_LOGGING")]
    public static void NotCleanlyDismounted(ILogger logger, string replay) { }

    [Conditional("REFS_LOGGING")]
    public static void StreamChecksumRule(ILogger logger, string rule) { }

    [Conditional("REFS_LOGGING")]
    public static void MountFailed(ILogger logger, string reason) { }

    [Conditional("REFS_LOGGING")]
    public static void ContainerTable(ILogger logger, int count, int bits, int compressed) { }

    [Conditional("REFS_LOGGING")]
    public static void ContainerRow(ILogger logger, ulong id, ulong start, uint clusters, ContainerFlags flags, CompressionFormat format) { }

    [Conditional("REFS_LOGGING")]
    public static void OpeningCompressedContainer(ILogger logger, ulong id, string format, uint chunkSize, int runs) { }

    [Conditional("REFS_LOGGING")]
    public static void PageRead(ILogger logger, ulong lcn, int clusters, ChecksumKind kind, string source) { }

    [Conditional("REFS_LOGGING")]
    public static void PageChecksumFailed(ILogger logger, ulong lcn, ChecksumKind kind) { }

    [Conditional("REFS_LOGGING")]
    public static void DirectoryListed(ILogger logger, ulong objectId, string replay) { }

    [Conditional("REFS_LOGGING")]
    public static void FileOpened(ILogger logger, string name, ulong fileId, ulong length, int runs, long clusters, int integrityRuns, string replay) { }

    [Conditional("REFS_LOGGING")]
    public static void RunListed(ILogger logger, int index, ulong vcn, uint count, ulong lcn, EntryProperties flags) { }

    [Conditional("REFS_LOGGING")]
    public static void ResidentOpened(ILogger logger, string name, ulong length) { }

    [Conditional("REFS_LOGGING")]
    public static void FileRead(ILogger logger, long position, int count, int run, string verified) { }

    [Conditional("REFS_LOGGING")]
    public static void DataChecksumFailed(ILogger logger, ulong lcn, string name, string kind) { }

    [Conditional("REFS_LOGGING")]
    public static void DataChecksumPlaceholder(ILogger logger, ulong lcn, string name, string kind) { }

    [Conditional("REFS_LOGGING")]
    public static void LogScanned(ILogger logger, long pages, ulong start, ulong startLsn, ulong lap, ulong endLsn, int entries, int transactions, int committed) { }

    [Conditional("REFS_LOGGING")]
    public static void Replaying(ILogger logger, int committed) { }

    [Conditional("REFS_LOGGING")]
    public static void NoLogInformation(ILogger logger) { }

    [Conditional("REFS_LOGGING")]
    public static void RedoRecord(ILogger logger, string operation, ulong lsn, string path) { }

    [Conditional("REFS_LOGGING")]
    public static void ReplaySkip(ILogger logger, int recordCount, ulong lsn) { }

    [Conditional("REFS_LOGGING")]
    public static void ReplayCreatedObject(ILogger logger, ulong objectId, ulong lsn) { }

    [Conditional("REFS_LOGGING")]
    public static void ReplayDeletedObject(ILogger logger, ulong objectId, ulong lsn) { }

    [Conditional("REFS_LOGGING")]
    public static void ReplayIgnoredCreateTable(ILogger logger, ulong objectId) { }

    [Conditional("REFS_LOGGING")]
    public static void ReplayIgnoredEmbeddedDelete(ILogger logger, ulong objectId, ulong lsn) { }

    [Conditional("REFS_LOGGING")]
    public static void ReplayCreatedRow(ILogger logger, ulong objectId, ulong lsn) { }

    [Conditional("REFS_LOGGING")]
    public static void ReplayIncomplete(ILogger logger, int count) { }

    [Conditional("REFS_LOGGING")]
    public static void ReplayApplied(ILogger logger, int tables, int rows) { }

    [Conditional("REFS_LOGGING")]
    public static void ValidationStarted(ILogger logger, bool fileData) { }

    [Conditional("REFS_LOGGING")]
    public static void Preloaded(ILogger logger, int pages, int threads, double milliseconds, int failed) { }

    [Conditional("REFS_LOGGING")]
    public static void ValidationPhase(ILogger logger, string phase, long pages, double milliseconds) { }

    [Conditional("REFS_LOGGING")]
    public static void ValidationIssue(ILogger logger, string location, string message) { }

    [Conditional("REFS_LOGGING")]
    public static void ValidationWarning(ILogger logger, string message) { }

    [Conditional("REFS_LOGGING")]
    public static void ValidationFinished(ILogger logger, int objects, ulong pages, int files, int problems) { }

    [Conditional("REFS_LOGGING")]
    public static void ValidatingDirectory(ILogger logger, ulong objectId, string path) { }

    [Conditional("REFS_LOGGING")]
    public static void SuperBlockCopy(ILogger logger, ulong lcn, ulong generation) { }

    [Conditional("REFS_LOGGING")]
    public static void SuperBlockInvalid(ILogger logger, ulong lcn, string reason) { }

    [Conditional("REFS_LOGGING")]
    public static void VolumeInformationRead(ILogger logger, ulong objectId, string label, Version volumeVersion, Version driverVersion, VolumeFlags flags, DateTimeOffset created,
        DateTimeOffset mounted) { }

    [Conditional("REFS_LOGGING")]
    public static void VolumeInformationFailover(ILogger logger, ulong objectId, ChecksumKind kind, ulong lcn, ulong partnerId) { }

    [Conditional("REFS_LOGGING")]
    public static void MirroredTableFailover(ILogger logger, string table, ulong objectId, ChecksumKind kind, ulong lcn, ulong partnerId) { }

    [Conditional("REFS_LOGGING")]
    public static void IoRead(ILogger logger, long offset, int length, int read, double milliseconds) { }

    [Conditional("REFS_LOGGING")]
    public static void IoSummary(ILogger logger, long reads, double megabytes, double milliseconds) { }

    [Conditional("REFS_LOGGING")]
    public static void ReplaySkippedLaterDeleted(ILogger logger, RedoOperation operation, ulong objectId, ulong lsn, ulong deleteLsn) { }

    [Conditional("REFS_LOGGING")]
    public static void ReplaySkippedTransaction(ILogger logger, int count, ulong lsn, RedoOperation operation, string reason) { }

    [Conditional("REFS_LOGGING")]
    public static void ReplayIgnoredBookkeeping(ILogger logger, RedoOperation operation, ulong objectId) { }

    [Conditional("REFS_LOGGING")]
    public static void ReplayMissingRow(ILogger logger, RedoOperation operation, ulong objectId, ulong lsn) { }
}