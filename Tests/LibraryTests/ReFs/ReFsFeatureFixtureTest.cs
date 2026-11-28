using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DiscUtils.ReFs;
using DiscUtils.ReFs.Diagnostics;
using DiscUtils.ReFs.Diagnostics.Enums;
using DiscUtils.ReFs.Entries.Enums;
using DiscUtils.ReFs.Entries.Streams;
using static LibraryTests.ReFs.ReFsFixtureTest;

namespace LibraryTests.ReFs;

/// <summary>Windows-specific features and redo-log replay, checked against externally created images (see <see cref="ReFsFixtureTest"/>).</summary>
public class ReFsFeatureFixtureTest
{
    private static string Json(string name) => File.ReadAllText(FixturePath(name));

    [FixtureFact("fixture4k-none")]
    public void ReportsFormatVersionAndVolumeInformation()
    {
        using var fs = OpenVolume("fixture4k-none", out var disk);
        using (disk)
        {
            Assert.Equal(3, fs.FormatVersion.Major);
            Assert.False(fs.ReplayEnabled);
            Assert.NotEqual(0u, fs.VolumeId);
            Assert.False(string.IsNullOrEmpty(fs.VolumeLabel) && fs.VolumeLabel is null);
        }
    }

    [FixtureFact("fixture4k-none")]
    public void ReportsSecurityDescriptorsReparsePointsAndHardLinks()
    {
        using var fs = OpenVolume("fixture4k-none", out var disk);
        using (disk)
        {
            var security = fs.GetSecurity(@"testdata\ads\a.txt");
            Assert.NotNull(security);
            Assert.NotNull(security!.Owner);

            var reparse = fs.GetReparsePoint(@"testdata\links\sym.txt");
            Assert.NotNull(reparse);
            Assert.Equal(unchecked((int)0xA000000C), reparse!.Tag);
            Assert.Null(fs.GetReparsePoint(@"testdata\links\target.txt"));

            Assert.Equal(2, fs.GetHardLinkCount(@"testdata\links\hard.txt"));
            Assert.Equal(2, fs.GetHardLinkCount(@"testdata\links\target.txt"));
            Assert.True(fs.HasHardLinks(@"testdata\links\hard.txt"));
            Assert.Equal(1, fs.GetHardLinkCount(@"testdata\ads\a.txt"));

            var info = fs.GetFileStandardInformation(@"testdata\ads\a.txt");
            Assert.Equal(fs.GetCreationTimeUtc(@"testdata\ads\a.txt"), info.CreationTime);
            Assert.Null(fs.GetShortName(@"testdata\ads\a.txt"));

            Assert.Throws<NotSupportedException>(() => fs.SetSecurity(@"testdata\ads\a.txt", security));
        }
    }

    [FixtureFact("fixture4k-ea")]
    public void ReadsExtendedAttributes()
    {
        // The manifest lists, per file, {"name": ..., "flags": ..., "value": base64}.
        var text = Json("fixture4k-ea.ea.json");
        // Split at the top-level keys ("  \"path\": ["); a file without EAs has an empty array.
        var parts = Regex.Split(text, @"(?m)^  ""(?<path>[^""]+)"":\s*\[");
        var files = new List<(string Path, string Body)>();
        for (var k = 1; k + 1 < parts.Length; k += 2)
        {
            files.Add((parts[k], parts[k + 1]));
        }

        Assert.NotEmpty(files);

        using var fs = OpenVolume("fixture4k-ea", out var disk);
        using (disk)
        {
            foreach (var file in files)
            {
                var relative = file.Path.Replace("\\\\", "\\");
                var path = fs.FileExists(@"testdata\" + relative) ? @"testdata\" + relative : relative;
                var expected = Regex.Matches(
                        file.Body,
                        "\"name\":\\s*\"(?<name>[^\"]*)\",\\s*\"flags\":\\s*(?<flags>\\d+),\\s*\"value\":\\s*\"(?<value>[^\"]*)\"")
                    .Cast<Match>()
                    .Select(m => (m.Groups["name"].Value, int.Parse(m.Groups["flags"].Value), Convert.FromBase64String(m.Groups["value"].Value)))
                    .ToList();

                var actual = fs.GetExtendedAttributes(path);
                Assert.True(expected.Count == actual.Count, $"{path}: expected {expected.Count} EAs, found {actual.Count}");

                for (var i = 0; i < expected.Count; i++)
                {
                    Assert.Equal(expected[i].Item1, actual[i].Name, ignoreCase: true); // Windows stores EA names in upper case
                    Assert.Equal((expected[i].Item2 & 0x80) != 0, actual[i].NeedEa);
                    Assert.Equal(expected[i].Item3, actual[i].Value);
                }
            }
        }
    }

    [FixtureFact("fixture4k-dirty")]
    public void ReplayShowsFilesCommittedAfterTheCheckpoint()
    {
        var names = Regex.Matches(Json("fixture4k-dirty.dirty.json"), "\"((?:flushed|unflushed)\\\\\\\\[^\"]+)\"")
            .Cast<Match>().Select(m => m.Groups[1].Value.Replace("\\\\", "\\")).ToList();
        Assert.NotEmpty(names);

        int visibleWithout;
        using (var plain = OpenVolume("fixture4k-dirty", out var disk1))
        using (disk1)
        {
            visibleWithout = names.Count(plain.FileExists);
            Assert.False(plain.ReplayEnabled);
        }

        using var replayed = OpenVolume("fixture4k-dirty", out var disk2, new ReFsFileSystemOptions { ReplayLog = true });
        using (disk2)
        {
            Assert.True(replayed.ReplayEnabled);

            var visibleWith = 0;
            foreach (var name in names.Where(replayed.FileExists))
            {
                visibleWith++;
                try
                {
                    using var stream = replayed.OpenFile(name, FileMode.Open, FileAccess.Read);
                    stream.CopyTo(Stream.Null);
                }
                catch (NotSupportedException)
                {
                    // Data allocated in the log cannot be read: documented limitation.
                }
            }

            // Files that only exist through the log are reported as replayed or partial; the checkpoint's files are unchanged.
            var states = names.Where(replayed.FileExists).Select(replayed.GetReplayState).ToList();
            Assert.Contains(states, st => st != ReFsReplayState.None);

            Assert.True(visibleWith > visibleWithout, $"replay should reveal files committed after the checkpoint: {visibleWith} with replay, {visibleWithout} without");
        }
    }

    [FixtureFact("fixture4k-none")]
    public void ValidatesCleanVolumeIncludingData()
    {
        using var fs = OpenVolume("fixture4k-none", out var disk);
        using (disk)
        {
            var report = fs.Validate();
            Assert.True(report.IsValid, string.Join("; ", report.Issues.Select(i => i.Location + ": " + i.Message)));
            Assert.True(report.Files > 1000);
            Assert.True(report.DataBytes > 0);
            Assert.True(report.MetadataChecksumsEnabled);
        }
    }

    [FixtureFact("fixture64k-zstd-1m")]
    public void ValidatesCompressedVolume()
    {
        using var fs = OpenVolume("fixture64k-zstd-1m", out var disk);
        using (disk)
        {
            var report = fs.Validate();
            Assert.True(report.IsValid, string.Join("; ", report.Issues.Select(i => i.Location + ": " + i.Message)));
        }
    }

    [FixtureFact("fixture4k-none")]
    public void ValidatesMetadataOfEveryLabImage()
    {
        // Only runs fully with REFS_FIXTURE_FULL=1: every VHDX in the fixture folder is opened and its metadata validated.
        if (Environment.GetEnvironmentVariable("REFS_FIXTURE_FULL") != "1")
        {
            return;
        }

        var failures = new List<string>();

        foreach (var file in Directory.GetFiles(Root!, "*.vhdx").OrderBy(f => f))
        {
            var image = Path.GetFileNameWithoutExtension(file);
            if (image.Contains("dirty") || image.Contains("devdrive"))
            {
                continue; // dirty images are covered by the replay tests; a Dev Drive volume may use another layout
            }

            try
            {
                using var fs = OpenVolume(image, out var disk);
                using (disk)
                {
                    var report = fs.Validate(verifyFileData: false);
                    if (!report.IsValid)
                    {
                        failures.Add($"{image}: {string.Join("; ", report.Issues.Take(3).Select(i => i.Location + ": " + i.Message))}");
                    }
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{image}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [FixtureFact("fixture4k-none")]
    public void ChecksumVerificationCanBeDisabled()
    {
        using var fs = OpenVolume("fixture4k-none", out var disk, new ReFsFileSystemOptions { VerifyChecksums = false, VerifyDataChecksums = false });
        using (disk)
        {
            Assert.True(fs.FileExists(@"testdata\ads\a.txt"));
        }
    }

    [FixtureFact("fixture4k-none")]
    public void DiagnosticsDescribeTheVolumeAndItsEntries()
    {
        using var fs = OpenVolume("fixture4k-none", out var disk);
        using (disk)
        {
            var volume = fs.GetVolumeDetails();
            Assert.Equal(fs.FormatVersion, volume.FormatVersion);
            Assert.Equal(4096u, volume.ClusterSize);
            Assert.NotEqual(ReFsChecksumKind.None, volume.MetadataChecksum);
            Assert.Equal(fs.VolumeLabel, volume.Label);
            Assert.False(volume.ReplayEnabled);

            var file = fs.GetEntryDetails(@"testdata\ads\a.txt");
            Assert.False(file.IsDirectory);
            Assert.Contains(file.Streams, s => s.Name.Length == 0);
            Assert.Contains(file.Streams, s => s.Name == "large");

            using (var viaName = fs.OpenStream(@"testdata\ads\a.txt", "large"))
            using (var viaPath = fs.OpenFile(@"testdata\ads\a.txt:large", FileMode.Open, FileAccess.Read))
            {
                Assert.Equal(Sha256Hex(viaPath), Sha256Hex(viaName));
            }

            Assert.True(fs.GetEntryDetails("testdata").IsDirectory);
            Assert.Equal(fs.GetFileId("testdata"), (long)fs.GetEntryDetails("testdata").ObjectId);
        }
    }

    [FixtureFact("w2028pre-xxh64-integrity")]
    public void DiagnosticsVerifyFileDataAndOpenSnapshots()
    {
        using var fs = OpenVolume("w2028pre-xxh64-integrity", out var disk);
        using (disk)
        {
            Assert.Equal(ReFsChecksumKind.Xxh64, fs.GetVolumeDetails().DataChecksum);

            long reported = 0;
            var result = fs.VerifyData(@"data\big.bin", new Progress(n => reported += n));
            Assert.True(result.HasChecksums);
            Assert.Contains(ReFsChecksumKind.Xxh64, result.Kinds);
            Assert.Equal((long)result.Bytes, reported);

            var tail = fs.GetEntryDetails(@"data\tail.bin");
            var snapshot = Assert.Single(tail.Streams, s => s.IsSnapshot);
            Assert.Equal("before-truncate", snapshot.Name);
            Assert.Equal(70001ul, snapshot.Length);
            using var data = fs.OpenStream(@"data\tail.bin", snapshot.Name);
            Assert.Equal("F3D2BAF50A69FA1CDD0EA4E25E24C2BA2CDC3E338861C62076D9C580DE288446", Sha256Hex(data));
        }
    }

    [FixtureFact("fixture4k-dirty")]
    public void DiagnosticsReadTheRedoLog()
    {
        using var fs = OpenVolume("fixture4k-dirty", out var disk);
        using (disk)
        {
            var log = fs.ReadRedoLog();
            Assert.NotNull(log);
            Assert.True(log!.HasWorkToReplay);
            Assert.True(log.RegionPages > 0);
            var record = log.Transactions.First(t => t.Committed).Records[0];
            Assert.False(string.IsNullOrEmpty(record.Operation));
            Assert.Contains(log.Transactions.SelectMany(t => t.Records).SelectMany(r => r.Path), c => c.Kind == ReFsRedoPathComponent.RootKind && c.ObjectId != 0);
        }
    }

    [FixtureFact("fixture4k-dirty")]
    public void ReplayCanStopAfterAnyTransaction()
    {
        // What the browser's log stepping relies on: 0 transactions shows the checkpoint, every committed one the full replay.
        static List<string> Listing(ReFsFileSystem fs) =>
            fs.GetFiles(string.Empty, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

        List<string> plain, none, all, full;
        int committed;
        using (var fs = OpenVolume("fixture4k-dirty", out var disk))
        using (disk)
        {
            plain = Listing(fs);
            committed = fs.ReadRedoLog()!.Transactions.Count(t => t.Committed);
        }

        using (var fs = OpenVolume("fixture4k-dirty", out var disk, new ReFsFileSystemOptions { ReplayLog = true, ReplayTransactions = 0 }))
        using (disk)
        {
            none = Listing(fs);
        }

        using (var fs = OpenVolume("fixture4k-dirty", out var disk, new ReFsFileSystemOptions { ReplayLog = true, ReplayTransactions = committed }))
        using (disk)
        {
            all = Listing(fs);
        }

        using (var fs = OpenVolume("fixture4k-dirty", out var disk, new ReFsFileSystemOptions { ReplayLog = true }))
        using (disk)
        {
            full = Listing(fs);
        }

        Assert.Equal(plain, none);
        Assert.Equal(full, all);
        Assert.NotEqual(plain, full);
    }

    /// <summary>
    /// ReFS deduplication shared the clusters of identical files, and volume compression then packed the containers holding them: the files'
    /// runs point at the same clusters, and those clusters lie in compressed containers. Contents are compared with Windows' by the manifest test.
    /// </summary>
    [FixtureFact("w2025-refsdedup-shared-lz4")]
    public void DeduplicatedFilesShareClustersInsideCompressedContainers()
    {
        using var fs = OpenVolume("w2025-refsdedup-shared-lz4", out var disk);
        using (disk)
        {
            var volume = fs.Volume;
            var a = Clusters(volume, @"d\a.bin");
            var b = Clusters(volume, @"d\b.bin");
            var c = Clusters(volume, @"d\sub\c.bin");
            var withAds = Clusters(volume, @"d\withads.bin");
            var text1 = Clusters(volume, @"d\text1.txt");
            var text2 = Clusters(volume, @"d\text2.txt");

            // a.bin, sub\c.bin and withads.bin are the same 1 MiB; b.bin was patched after the dedup job, which moved 3 of its clusters.
            Assert.Equal(256, a.Count);
            Assert.Equal(256, a.Intersect(c).Count());
            Assert.Equal(256, a.Intersect(withAds).Count());
            Assert.Equal(253, a.Intersect(b).Count());
            Assert.Equal(621, text1.Intersect(text2).Count());

            // The shared clusters are in compressed containers; b.bin's 3 patched clusters are not shared and lie in an uncompressed one.
            var shared = new HashSet<ulong>(a.Intersect(b).Union(a.Intersect(c)).Union(a.Intersect(withAds)).Union(text1.Intersect(text2)));
            foreach (var lcn in shared)
            {
                Assert.True(volume.Containers.TryGetCompressed(lcn, out _), $"shared cluster 0x{lcn:X} is not in a compressed container");
            }

            Assert.All(b.Except(a), lcn => Assert.False(volume.Containers.TryGetCompressed(lcn, out _), $"patched cluster 0x{lcn:X} is in a compressed container"));
        }
    }

    /// <summary>The container clusters of the file's unnamed stream.</summary>
    private static HashSet<ulong> Clusters(ReFsVolume volume, string path)
    {
        Assert.True(volume.TryGetEntry(path, out var entry), path);
        using var stream = volume.OpenRead(entry);
        var runs = Assert.IsType<RunFileStream>(stream);
        var clusters = new HashSet<ulong>();
        foreach (var run in runs.Runs)
        {
            for (ulong i = 0; i < run.Count; i++)
            {
                clusters.Add(run.Lcn + i);
            }
        }

        return clusters;
    }

    private sealed class Progress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
