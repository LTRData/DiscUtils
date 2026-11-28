using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using DiscUtils;
using DiscUtils.ReFs;
using DiscUtils.Vhdx;

namespace LibraryTests.ReFs;

/// <summary>
/// Runs against externally created Windows ReFS images (not checked in). Set REFS_FIXTURE_ROOT to the folder holding
/// <c>fixture4k-none.vhdx</c>, <c>fixture64k-none.vhdx</c>, the compressed fixtures and their <c>*.manifest.json</c> files.
/// Tests are skipped when the variable is unset or the images are missing. REFS_FIXTURE_FULL=1 hashes every file
/// instead of a sample.
/// </summary>
public class ReFsFixtureTest
{
    internal static readonly string? Root = Environment.GetEnvironmentVariable("REFS_FIXTURE_ROOT");

    private static readonly bool Full = Environment.GetEnvironmentVariable("REFS_FIXTURE_FULL") == "1";

    /// <summary>A file of the lab dataset. Only the folder itself is read: its <c>unused</c> folder holds retired images that no test needs.</summary>
    internal static string FixturePath(string fileName) => Path.Combine(Root!, fileName);

    /// <summary>
    /// A test of the lab dataset: skipped when <c>REFS_FIXTURE_ROOT</c> is not set and, when <paramref name="image"/> is given, when that image
    /// (<c>&lt;image&gt;.vhdx</c>) is not in the folder. Without an image the test walks the folder itself.
    /// </summary>
    public sealed class FixtureFactAttribute : FactAttribute
    {
        public FixtureFactAttribute(string? image = null)
        {
            if (string.IsNullOrEmpty(Root))
            {
                Skip = "REFS_FIXTURE_ROOT is not set.";
            }
            else if (image is not null && !File.Exists(FixturePath(image + ".vhdx")))
            {
                Skip = $"Fixture {image}.vhdx is not available in {Root}.";
            }
        }
    }

    private sealed record ManifestRow(string Path, long Length, string Sha256, FileAttributes Attributes, DateTime Created, DateTime Modified);

    /// <summary>
    /// The files of a fixture's <c>&lt;image&gt;.manifest.json</c> (paths relative to the volume root, as Windows listed them). These manifests
    /// list only the files under <c>testdata</c>, with their length, SHA-256, attributes and FILETIME creation and write times.
    /// </summary>
    private static List<ManifestRow> LoadManifest(string image)
    {
        return LabManifest.Entries(LabManifest.ForImage(image))
            .Where(e => !LabManifest.Bool(e, "directory"))
            .Select(e => new ManifestRow(
                LabManifest.Str(e, "path")!,
                LabManifest.Num(e, "length"),
                LabManifest.Str(e, "sha256")!,
                (FileAttributes)LabManifest.Num(e, "attributes"),
                DateTime.FromFileTimeUtc(LabManifest.Num(e, "created")),
                DateTime.FromFileTimeUtc(LabManifest.Num(e, "modified"))))
            .ToList();
    }

    internal static string Sha256Hex(Stream stream)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
    }

    internal static ReFsFileSystem OpenVolume(string image, out Disk disk, ReFsFileSystemOptions? options = null)
    {
        DiscUtils.ReFs.Formats.Register();
        disk = new Disk(FixturePath(image + ".vhdx"), FileAccess.Read);
        var volumes = new VolumeManager(disk).GetLogicalVolumes();
        var volume = volumes.FirstOrDefault(v => FileSystemManager.DetectFileSystems(v).Any(i => i.Name == "ReFS"))
            ?? throw new InvalidOperationException($"{image}: no ReFS volume found.");
        return options is null ? new ReFsFileSystem(volume.Open()) : new ReFsFileSystem(volume.Open(), options);
    }

    [FixtureFact("fixture4k-none")]
    public void Lists4kVolume() => CheckListing("fixture4k-none");

    [FixtureFact("fixture64k-none")]
    public void Lists64kVolume() => CheckListing("fixture64k-none");

    private static void CheckListing(string image)
    {
        using var fs = OpenVolume(image, out var disk);
        using (disk)
        {
            Assert.False(fs.CanWrite);
            Assert.NotEmpty(fs.Root.GetDirectories());

            const FileAttributes Compared = ~(FileAttributes.Normal | FileAttributes.IntegrityStream);

            foreach (var row in LoadManifest(image))
            {
                var path = row.Path;
                Assert.True(fs.FileExists(path), $"{path} not found");
                Assert.Equal(row.Length, fs.GetFileLength(path));
                Assert.Equal(row.Attributes & Compared, fs.GetAttributes(path) & Compared);
                Assert.Equal(row.Created, fs.GetCreationTimeUtc(path));
                Assert.Equal(row.Modified, fs.GetLastWriteTimeUtc(path));
            }
        }
    }

    [FixtureFact("fixture4k-none")]
    public void ReadsContent4k() => CheckContent("fixture4k-none");

    [FixtureFact("fixture64k-none")]
    public void ReadsContent64k() => CheckContent("fixture64k-none");

    private static void CheckContent(string image)
    {
        using var fs = OpenVolume(image, out var disk);
        using (disk)
        {
            // The manifest hashes the target of reparse points (symbolic links), which this reader does not follow.
            var rows = LoadManifest(image).Where(r => (r.Attributes & FileAttributes.ReparsePoint) == 0).ToList();

            // Without REFS_FIXTURE_FULL, spread a sample over the manifest (small and large files).
            var step = Full ? 1 : Math.Max(1, rows.Count / 200);
            var hashed = 0;

            for (var i = 0; i < rows.Count; i += step)
            {
                var row = rows[i];
                var path = row.Path;

                using var stream = fs.OpenFile(path, FileMode.Open, FileAccess.Read);
                var hash = Sha256Hex(stream);
                hashed++;

                Assert.True(
                    string.Equals(row.Sha256, hash, StringComparison.OrdinalIgnoreCase),
                    $"{path}: length {row.Length}, expected {row.Sha256}, actual {hash}");
            }

            Assert.True(hashed >= (Full ? rows.Count : 100), $"only {hashed} of {rows.Count} files were hashed");
        }
    }

    [FixtureFact("fixture4k-none")]
    public void AlternateStreamsAreListedAndReadable()
    {
        using var fs = OpenVolume("fixture4k-none", out var disk);
        using (disk)
        {
            var streams = fs.GetAlternateDataStreams(@"testdata\ads\a.txt").ToList();
            Assert.Contains("large", streams);
            Assert.Contains("small", streams);

            // The 100000-byte stream is stored in runs, the 9-byte one inline.
            var source = LoadManifest("fixture4k-none").Single(r => r.Path == @"testdata\ads\big_ads_source.bin");
            using (var large = fs.OpenFile(@"testdata\ads\a.txt:large", FileMode.Open, FileAccess.Read))
            {
                Assert.Equal(100000, large.Length);
                Assert.Equal(source.Sha256, Sha256Hex(large), ignoreCase: true);
            }

            using var small = fs.OpenFile(@"testdata\ads\a.txt:small", FileMode.Open, FileAccess.Read);
            Assert.Equal(9, small.Length);
            var buffer = new byte[9];
            Assert.Equal(9, small.Read(buffer, 0, 9));

            Assert.Throws<FileNotFoundException>(() => fs.OpenFile(@"testdata\ads\a.txt:missing", FileMode.Open, FileAccess.Read));
        }
    }

    [FixtureFact("fixture4k-none")]
    public void OpensReadOnlyAndDetectedThroughGenericApis()
    {
        DiscUtils.ReFs.Formats.Register();

        using var disk = new Disk(FixturePath("fixture4k-none.vhdx"), FileAccess.Read);
        var volume = new VolumeManager(disk).GetLogicalVolumes()
            .First(v => FileSystemManager.DetectFileSystems(v).Any(i => i.Name == "ReFS"));

        var info = FileSystemManager.DetectFileSystems(volume).First(i => i.Name == "ReFS");
        using var fs = info.Open(volume);

        Assert.IsType<ReFsFileSystem>(fs);
        Assert.False(fs.CanWrite);
        Assert.True(fs.SupportsUsedAvailableSpace);
        Assert.True(fs.Size > 0);
        Assert.True(fs.UsedSpace > 0 && fs.AvailableSpace > 0 && fs.UsedSpace + fs.AvailableSpace <= fs.Size);
        Assert.Throws<NotSupportedException>(() => fs.DeleteFile(@"testdata\ads\a.txt"));
        Assert.True(fs.FileExists(@"testdata\ads\a.txt"));
    }

    [FixtureFact("fixture4k-lz4")]
    public void ReadsLz4Compressed4k() => CheckContent("fixture4k-lz4");

    [FixtureFact("fixture64k-lz4")]
    public void ReadsLz4Compressed64k() => CheckContent("fixture64k-lz4");

    [FixtureFact("fixture64k-zstd-1m")]
    public void ReadsZstd1mCompressed64k() => CheckContent("fixture64k-zstd-1m");

    [Fact]
    public void Lz4BlockDecoderHandlesOverlapAndRejectsBadInput()
    {
        // 1 literal 'a', then a match of length 9 at offset 1 (overlapping), then the mandatory final literals-only sequence.
        byte[] block = [0x15, (byte)'a', 0x01, 0x00, 0x10, (byte)'b'];
        var output = new byte[11];
        Assert.Equal(11, DiscUtils.ReFs.Compression.ManagedChunkDecompressor.Lz4Block(block, output));
        Assert.Equal("aaaaaaaaaab", System.Text.Encoding.ASCII.GetString(output));

        // Match offset beyond the produced output.
        Assert.ThrowsAny<InvalidFileSystemException>(() =>
            DiscUtils.ReFs.Compression.ManagedChunkDecompressor.Lz4Block(new byte[] { 0x10, (byte)'a', 0x05, 0x00 }, new byte[32]));

        // Output buffer too small.
        Assert.ThrowsAny<InvalidFileSystemException>(() =>
            DiscUtils.ReFs.Compression.ManagedChunkDecompressor.Lz4Block(block, new byte[4]));
    }
}