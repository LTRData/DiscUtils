using System.IO;
using DiscUtils.DeduplicationRole;
using DiscUtils.Streams;

namespace DiscUtils.Ntfs.Internals;

/// <summary>
/// The reparse plugin for files optimized by the Windows Server Data Deduplication role (<c>IO_REPARSE_TAG_DEDUP</c>): the file's unnamed data
/// stream, opened for reading, is read from the volume's chunk store, with the ranges written since the optimization taken from the stub
/// (<see cref="DedupReader"/>). Anything else (named streams, write access, <see cref="NtfsOptions.ReadDeduplicatedFiles"/> off) opens the stub.
/// </summary>
internal static class Deduplication
{
    public const uint ReparsePointTagDedup = DedupReader.ReparseTag;

    public static SparseStream OpenDedupReparsePoint(File file,
                                                     StandardInformation info,
                                                     NtfsAttribute attr,
                                                     FileAccess access,
                                                     NtfsStream ntfsStream,
                                                     ReparsePointRecord reparsePoint)
    {
        if (access != FileAccess.Read
            || attr.PrimaryRecord.AttributeType != AttributeType.Data
            || !string.IsNullOrEmpty(attr.PrimaryRecord.Name)
            || file.Context is not { Options.ReadDeduplicatedFiles: true, Deduplication: { } reader })
        {
            return null;
        }

        var stub = attr.Open(FileAccess.Read);
        var content = reader.Open(reparsePoint.Content, stub);
        return content as SparseStream ?? SparseStream.FromStream(content, Ownership.Dispose);
    }
}
