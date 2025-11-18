// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

namespace LibraryTests.ExFat;

public static class DiskContent
{
    public const string VolumeLabel = "Some ExFAT";

    public const ulong LongFileSize = 1 << 20;
    public const string LongContiguousFileName = "1M contiguous";
    public static ulong GetLongContiguousFileNameOffsetValue(ulong offset) => offset;

    public const string LongSparseFile1Name = "1M sparse 1";
    public static ulong GetLongSparseFile1NameOffsetValue(ulong offset) => offset / 8 * 3;
    public const string LongSparseFile2Name = "1M sparse 2 (the evil twin)";
    public static ulong GetLongSparseFile2NameOffsetValue(ulong offset) => offset / 8 * 5;

    public const string EmptyRootFolderFileName = "Empty folder";

    // one directory cluster contains up to 4096 entries
    // a file contains 3 entries: file, stream and name
    // 1400 * 3 = 4200, which is > 4096
    public const int LongFolderEntriesCount = 1400;

    public const string LongFolderFileName = "Big folder";
}