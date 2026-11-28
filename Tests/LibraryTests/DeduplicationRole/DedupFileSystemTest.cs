using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using DiscUtils;
using DiscUtils.DeduplicationRole;
using DiscUtils.Ntfs;
using DiscUtils.ReFs;
using DiscUtils.Streams;
using static LibraryTests.ReFs.ReFsFixtureTest;

namespace LibraryTests.DeduplicationRole;

/// <summary>
/// <see cref="DedupFileSystemReader"/> over a file system, with the chunk store and the optimized files of the lab image <c>w2025-dedup</c>
/// (Windows Server 2025, ReFS): read through the generic reader on the ReFS volume itself, and on an NTFS volume built in memory with a copy of the
/// chunk store, the sparse stubs (with the data written since the optimization) and the reparse points.
/// </summary>
public class DedupFileSystemTest
{
    private const string ChunkStore = @"System Volume Information\Dedup";

    [FixtureFact("w2025-dedup")]
    public void TheGenericReaderReadsOptimizedFilesOnReFs()
    {
        using var stored = OpenVolume("w2025-dedup", out var disk1, new ReFsFileSystemOptions { ReadDeduplicatedFiles = false });
        using var expected = OpenVolume("w2025-dedup", out var disk2);
        using (disk1)
        using (disk2)
        using (var reader = new DedupFileSystemReader(stored))
        using (var resolving = new DedupFileSystemReader(expected))
        {
            var files = OptimizedFiles(stored);
            Assert.NotEmpty(files);
            foreach (var path in files)
            {
                var want = Hash(expected.OpenFile(path, FileMode.Open, FileAccess.Read));
                Assert.True(reader.IsDeduplicated(path));
                Assert.Equal(want, Hash(reader.OpenRead(path)));

                // ReFS reads the file from the chunk store itself; the reader returns that as it is.
                Assert.True(resolving.IsDeduplicated(path));
                Assert.Equal(want, Hash(resolving.OpenRead(path)));
            }

            Assert.False(reader.IsDeduplicated(@"d\small.bin"));
        }
    }

    [FixtureFact("w2025-dedup")]
    public void TheGenericReaderReadsOptimizedFilesOnNtfs()
    {
        using var refs = OpenVolume("w2025-dedup", out var disk, new ReFsFileSystemOptions { ReadDeduplicatedFiles = false });
        using var expected = OpenVolume("w2025-dedup", out var disk2);
        using (disk)
        using (disk2)
        {
            const long capacity = 512L * 1024 * 1024;
            using var volume = new SparseMemoryStream();
            volume.SetLength(capacity);
            using var ntfs = NtfsFileSystem.Format(volume, "Dedup", Geometry.FromCapacity(capacity), 0, capacity / 512);

            // The chunk store as it is.
            foreach (var directory in new[] { ChunkStore }.Concat(refs.GetDirectories(ChunkStore, "*", SearchOption.AllDirectories)))
            {
                ntfs.CreateDirectory(directory);
            }

            foreach (var file in refs.GetFiles(ChunkStore, "*", SearchOption.AllDirectories))
            {
                using var source = refs.OpenFile(file, FileMode.Open, FileAccess.Read);
                using var target = ntfs.OpenFile(file, FileMode.Create, FileAccess.ReadWrite);
                source.CopyTo(target);
            }

            // Each optimized file as Windows leaves it: a sparse stub of the full length holding only the data written since the optimization,
            // and the Data Deduplication reparse point.
            var files = OptimizedFiles(refs);
            foreach (var path in files)
            {
                ntfs.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var stub = refs.OpenFile(path, FileMode.Open, FileAccess.Read))
                {
                    using (ntfs.OpenFile(path, FileMode.Create, FileAccess.ReadWrite))
                    {
                    }

                    ntfs.SetAttributes(path, ntfs.GetAttributes(path) | FileAttributes.SparseFile);
                    using var target = ntfs.OpenFile(path, FileMode.Open, FileAccess.ReadWrite);
                    target.SetLength(stub.Length);
                    var buffer = new byte[64 * 1024];
                    foreach (var extent in stub.Extents)
                    {
                        for (var position = extent.Start; position < extent.Start + extent.Length; position += buffer.Length)
                        {
                            var count = (int)Math.Min(buffer.Length, extent.Start + extent.Length - position);
                            stub.Position = position;
                            stub.ReadExactly(buffer, 0, count);
                            target.Position = position;
                            target.Write(buffer, 0, count);
                        }
                    }
                }

                ntfs.SetReparsePoint(path, refs.GetReparsePoint(path)!);
            }

            using var reader = new DedupFileSystemReader(ntfs);
            foreach (var path in files)
            {
                var want = Hash(expected.OpenFile(path, FileMode.Open, FileAccess.Read));
                // NTFS's own reparse plugin (NtfsOptions.ReadDeduplicatedFiles, on by default) reads the file; the reader returns that as it is.
                Assert.True(reader.IsDeduplicated(path), path);
                Assert.Equal(want, Hash(reader.OpenRead(path)));
                Assert.Equal(want, Hash(ntfs.OpenFile(path, FileMode.Open, FileAccess.Read)));
            }

            // Turned off, the files read as their stubs through OpenFile, and the reader reads them from the chunk store itself.
            ntfs.NtfsOptions.ReadDeduplicatedFiles = false;
            Assert.NotEqual(Hash(expected.OpenFile(files[0], FileMode.Open, FileAccess.Read)), Hash(ntfs.OpenFile(files[0], FileMode.Open, FileAccess.Read)));
            Assert.Equal(Hash(expected.OpenFile(files[0], FileMode.Open, FileAccess.Read)), Hash(reader.OpenRead(files[0])));
        }
    }

    private static List<string> OptimizedFiles(ReFsFileSystem fs)
    {
        return fs.GetFiles("d", "*", SearchOption.AllDirectories)
            .Where(path => fs.GetReparsePoint(path) is { } reparse && unchecked((uint)reparse.Tag) == DedupReader.ReparseTag)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Hash(Stream stream)
    {
        using (stream)
        using (var sha = SHA256.Create())
        {
            return BitConverter.ToString(sha.ComputeHash(stream));
        }
    }
}