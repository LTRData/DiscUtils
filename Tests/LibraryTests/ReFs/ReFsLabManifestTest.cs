using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiscUtils;
using DiscUtils.ReFs;
using static LibraryTests.ReFs.LabManifest;
using static LibraryTests.ReFs.ReFsFixtureTest;

namespace LibraryTests.ReFs;

/// <summary>
/// Compares the DiscUtils facade with the JSON manifests of the lab images in <c>REFS_FIXTURE_ROOT</c> (names, sizes and SHA-256 of every file and
/// named stream, as Windows read them): every clean image (the kitchen images of each Windows version, Data Deduplication, integrity streams,
/// upgraded volumes), and the cut-off copies before and after replay. The tests that walk the folder pass over the images that are not in it; a
/// test of one image is skipped when that image is missing, and every test when the folder is not set (<see cref="ReFsFixtureTest.FixtureFactAttribute"/>).
/// </summary>
public class ReFsLabManifestTest
{
    private const uint DedupTag = 0x80000013;
    private const int EncryptedAttribute = 0x4000;

    /// <summary>
    /// Every cleanly dismounted image in the folder, compared in full with its manifest. Cut-off copies and read-backs have their own tests, and
    /// a manifest that lists only some entries (<c>"complete": false</c>, the fixture manifests) is compared by <see cref="ReFsFixtureTest"/>.
    /// </summary>
    [FixtureFact]
    public void CleanImagesMatchWindows()
    {
        var failures = new List<string>();
        var images = 0;
        foreach (var manifestPath in Directory.GetFiles(Root!, "*.manifest.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            var manifest = Load(manifestPath);
            var name = Str(manifest, "name") ?? Path.GetFileName(manifestPath);
            var scenario = Str(manifest, "scenario") ?? string.Empty;
            var imagePath = FixturePath(Str(manifest, "image") ?? name + ".vhdx");
            if (IsCutOff(scenario) || scenario == "readback" || manifest.TryGetValue("complete", out var complete) && complete is false
                || !File.Exists(imagePath))
            {
                continue;
            }

            images++;
            try
            {
                using var fs = Open(imagePath, null, out var disk);
                using (disk)
                {
                    Compare(name, fs, Entries(manifest, "entries"), content: true, failures);
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(images > 0, $"No clean lab images in {Root}.");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(40)));
    }

    [FixtureFact("w2025-dedup")]
    public void DeduplicatedImageValidates() => CheckValidates("w2025-dedup");

    [FixtureFact("w2028pre-xxh64-integrity")]
    public void Xxh64IntegrityImageValidates() => CheckValidates("w2028pre-xxh64-integrity");

    private static void CheckValidates(string image)
    {
        using var fs = Open(FixturePath(image + ".vhdx"), null, out var disk);
        using (disk)
        {
            var report = fs.Validate();
            Assert.True(report.IsValid, string.Join("; ", report.Issues.Take(5).Select(i => i.Location + ": " + i.Message)));
        }
    }

    [FixtureFact("w2025-dedup")]
    public void DeduplicatedFilesReadAsStubsWhenTheReaderIsOff()
    {
        var manifest = ForImage("w2025-dedup");
        var imagePath = FixturePath("w2025-dedup.vhdx");
        var file = Entries(manifest, "entries").First(e => Reparse(e) == DedupTag && Str(e, "sha256") is not null);
        using var fs = Open(imagePath, new ReFsFileSystemOptions { ReadDeduplicatedFiles = false }, out var disk);
        using (disk)
        {
            var path = Str(file, "path")!;
            Assert.Equal(Num(file, "length"), fs.GetFileLength(path));
            using var stream = fs.OpenFile(path, FileMode.Open, FileAccess.Read);
            Assert.NotEqual(Str(file, "sha256"), Sha256Hex(stream));
        }
    }

    /// <summary>
    /// Copies taken while the volume was mounted (see <see cref="CutOffImage"/>, next to a "dirty*" manifest). Without replay the names and sizes are those of the last
    /// checkpoint ("before"); with replay, those Windows saw at the copy ("entries"), content included. As in the standalone reader, images without a
    /// "before" list (the churn copies, taken while changes were still running) are left out, and so are "dirtyopen" and "dirtytables": neither list
    /// describes their copy.
    /// </summary>
    [FixtureFact]
    public void CutOffCopiesMatchWindowsBeforeAndAfterReplay()
    {
        var failures = new List<string>();
        foreach (var (name, manifest, imagePath) in Manifests("*.manifest.json"))
        {
            var scenario = Str(manifest, "scenario") ?? string.Empty;
            if (!IsCutOff(scenario) || scenario is "dirtyopen" or "dirtytables"
                || !(manifest.TryGetValue("before", out var before) && before is List<object?>))
            {
                continue;
            }

            try
            {
                using (var fs = Open(imagePath, null, out var disk))
                using (disk)
                {
                    Compare(name + " (no replay)", fs, Entries(manifest, "before"), content: false, failures);
                }

                using (var fs = Open(imagePath, new ReFsFileSystemOptions { ReplayLog = true }, out var disk))
                using (disk)
                {
                    Compare(name + " (replay)", fs, Entries(manifest, "entries"), content: true, failures);
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(40)));
    }

    /// <summary>Copies Windows itself mounted and listed (<c>&lt;copy&gt;-readback.manifest.json</c>): with replay the facade shows what Windows recovered.</summary>
    [FixtureFact]
    public void CutOffCopiesMatchWindowsOwnReplay()
    {
        var failures = new List<string>();
        foreach (var (name, manifest, _) in Manifests("*-readback.manifest.json"))
        {
            var imagePath = CutOffImage(name.Substring(0, name.Length - "-readback".Length));
            if (Str(manifest, "scenario") != "readback" || !File.Exists(imagePath))
            {
                continue;
            }

            try
            {
                using var fs = Open(imagePath, new ReFsFileSystemOptions { ReplayLog = true }, out var disk);
                using (disk)
                {
                    Compare(name, fs, Entries(manifest, "entries"), content: true, failures);
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(40)));
    }

    private static void Compare(string image, ReFsFileSystem fs, List<Dictionary<string, object?>> entries, bool content, List<string> failures)
    {
        foreach (var entry in entries)
        {
            var path = Str(entry, "path")!;
            if (Bool(entry, "directory"))
            {
                if (!fs.DirectoryExists(path))
                {
                    failures.Add($"{image}: directory '{path}' missing");
                }

                continue;
            }

            if (!fs.FileExists(path))
            {
                failures.Add($"{image}: file '{path}' missing");
                continue;
            }

            // The listed size (standard information) can lag behind the size the stream reads with (w2025-dirtyopen); the manifest has both.
            var length = Num(entry, "length");
            var listed = entry.ContainsKey("listedLength") ? Num(entry, "listedLength") : length;
            if (fs.GetFileLength(path) != listed)
            {
                failures.Add($"{image}: '{path}' listed length {fs.GetFileLength(path)}, Windows {listed}");
            }

            // Windows does not read a link's own content (it follows it), but the dedup filter gives a deduplicated file's content.
            var reparse = Reparse(entry);
            if (!content || (reparse is not null && reparse != DedupTag))
            {
                continue;
            }

            var encrypted = ((int)Num(entry, "attributes") & EncryptedAttribute) != 0;
            CompareStream(image, fs, path, length, Str(entry, "sha256"), encrypted, failures);

            if (entry.TryGetValue("streams", out var streams) && streams is List<object?> list)
            {
                foreach (var stream in list.OfType<Dictionary<string, object?>>().Where(s => !Bool(s, "snapshot")))
                {
                    CompareStream(image, fs, path + ":" + Str(stream, "name"), Num(stream, "length"), Str(stream, "sha256"), encrypted, failures);
                }
            }
        }
    }

    private static void CompareStream(string image, ReFsFileSystem fs, string path, long length, string? sha256, bool encrypted, List<string> failures)
    {
        try
        {
            using var stream = fs.OpenFile(path, FileMode.Open, FileAccess.Read);
            if (encrypted && length != 0)
            {
                // The manifest holds the plaintext Windows decrypts; the reader must refuse the ciphertext.
                stream.ReadByte();
                failures.Add($"{image}: '{path}' is encrypted, but its data was read");
                return;
            }

            if (stream.Length != length)
            {
                failures.Add($"{image}: '{path}' stream length {stream.Length}, Windows {length}");
            }

            if (sha256 is not null && !string.Equals(Sha256Hex(stream), sha256, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{image}: '{path}' content differs from Windows");
            }
        }
        catch (NotSupportedException) when (encrypted)
        {
        }
        catch (Exception ex)
        {
            failures.Add($"{image}: '{path}': {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static ReFsFileSystem Open(string imagePath, ReFsFileSystemOptions? options, out VirtualDisk disk)
    {
        DiscUtils.ReFs.Formats.Register();
        disk = imagePath.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)
            ? new DiscUtils.Raw.Disk(imagePath, FileAccess.Read)
            : new DiscUtils.Vhdx.Disk(imagePath, FileAccess.Read);
        var volume = new VolumeManager(disk).GetLogicalVolumes()
            .FirstOrDefault(v => FileSystemManager.DetectFileSystems(v).Any(i => i.Name == "ReFS"))
            ?? throw new InvalidOperationException($"{imagePath}: no ReFS volume found.");
        return new ReFsFileSystem(volume.Open(), options ?? new ReFsFileSystemOptions());
    }

    /// <summary>A copy taken while the volume was mounted: the lab's dirty scenarios and the fixture ones (<c>fixture-dirty*</c>).</summary>
    private static bool IsCutOff(string scenario) =>
        scenario.StartsWith("dirty", StringComparison.Ordinal) || scenario.StartsWith("fixture-dirty", StringComparison.Ordinal);

    /// <summary>
    /// A cut-off copy: <c>&lt;name&gt;.vhdx</c>, the lossless sparse copy of the raw capture, when it exists, else <c>&lt;name&gt;.raw</c>.
    /// </summary>
    private static string CutOffImage(string name)
    {
        var sparse = FixturePath(name + ".vhdx");
        return File.Exists(sparse) ? sparse : FixturePath(name + ".raw");
    }

    private static IEnumerable<(string Name, Dictionary<string, object?> Manifest, string ImagePath)> Manifests(string pattern)
    {
        foreach (var manifestPath in Directory.GetFiles(Root!, pattern).OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(manifestPath);
            name = name.Substring(0, name.Length - ".manifest.json".Length);
            var imagePath = CutOffImage(name);
            if (pattern.Contains("readback") || File.Exists(imagePath))
            {
                yield return (name, Load(manifestPath), imagePath);
            }
        }
    }
}