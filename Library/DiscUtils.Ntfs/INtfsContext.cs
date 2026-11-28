using System.IO;

namespace DiscUtils.Ntfs;

internal interface INtfsContext
{
    AllocateFileFn AllocateFile { get; }

    AttributeDefinitions AttributeDefinitions { get; }

    BiosParameterBlock BiosParameterBlock { get; }

    ClusterBitmap ClusterBitmap { get; }

    ForgetFileFn ForgetFile { get; }

    GetDirectoryByIndexFn GetDirectoryByIndex { get; }

    GetDirectoryByRefFn GetDirectoryByRef { get; }

    GetFileByIndexFn GetFileByIndex { get; }

    GetFileByRefFn GetFileByRef { get; }

    MasterFileTable Mft { get; }

    ObjectIds ObjectIds { get; }

    NtfsOptions Options { get; }

    /// <summary>Reads files optimized by Data Deduplication from this volume's chunk store; null when the context has no file system.</summary>
    DeduplicationRole.DedupReader Deduplication { get; }

    Quotas Quotas { get; }
    Stream RawStream { get; }

    bool ReadOnly { get; }

    ReparsePoints ReparsePoints { get; }

    SecurityDescriptors SecurityDescriptors { get; }

    UpperCase UpperCase { get; }
}