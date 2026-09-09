//
// Copyright (c) 2008-2011, Kenneth Bell
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using DiscUtils.SquashFs;

namespace LibraryTests.SquashFs;

/// <summary>
/// The recipe of the SquashFS test images, as code: what the source trees hold and how mksquashfs turns them into
/// extended-inodes.sqsh and huge-sparse.sqsh. When squashfs-tools (4.5 or later) is on the machine, the images are
/// rebuilt and compared with the embedded ones, so the binaries in the repository are verifiable rather than
/// trusted: byte for byte when the installed mksquashfs is the release that made them, since other releases lay an
/// image out differently, and otherwise through the reader, file by file. Without the tool the test passes without
/// checking anything.
/// </summary>
public sealed class SquashFixtureRecipeTest
{
    private const string TextLine = "a line of text that compresses well, over and over\n";
    private const string FixtureToolVersion = "4.7.5";

    [Fact]
    public void EmbeddedImagesAreWhatMksquashfsMakesOfTheRecipe()
    {
        if (Environment.OSVersion.Platform != PlatformID.Unix || !ToolExists("mksquashfs") || !ToolExists("ln"))
        {
            return; // no squashfs-tools here: nothing to compare against
        }

        var work = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "squashfs-recipe-" + Path.GetRandomFileName()));
        try
        {
            var ext = Path.Combine(work.FullName, "ext");
            Directory.CreateDirectory(Path.Combine(ext, "dir"));
            File.WriteAllText(Path.Combine(ext, "small.txt"), "hello", Encoding.ASCII);
            File.WriteAllText(Path.Combine(ext, "text.bin"), Text(300000), Encoding.ASCII);
            using (var sparse = File.Create(Path.Combine(ext, "sparse.bin")))
            {
                sparse.SetLength(2L * 1024 * 1024);
                sparse.Position = sparse.Length;
                Write(sparse, "end");
            }
            File.WriteAllText(Path.Combine(ext, "hard.txt"), "twice", Encoding.ASCII);
            Run("ln", Path.Combine(ext, "hard.txt"), Path.Combine(ext, "dir", "hard2.txt"));

            var huge = Path.Combine(work.FullName, "huge");
            Directory.CreateDirectory(huge);
            using (var file = File.Create(Path.Combine(huge, "huge.bin")))
            {
                file.SetLength(4L * 1024 * 1024 * 1024); // sparse on disk
                file.Position = 2L * 1024 * 1024 * 1024 + 5;
                Write(file, "mid");
                file.Position = file.Length;
                Write(file, "end!");
            }

            var flags = new[] { "-noappend", "-no-xattrs", "-no-progress", "-quiet", "-mkfs-time", "0", "-all-time", "0", "-force-uid", "0", "-force-gid", "0" };
            var extImage = Path.Combine(work.FullName, "extended-inodes.sqsh");
            var hugeImage = Path.Combine(work.FullName, "huge-sparse.sqsh");
            Run("mksquashfs", new[] { ext, extImage, "-comp", "gzip", "-Xcompression-level", "6", "-b", "128K" }.Concat(flags).ToArray());
            Run("mksquashfs", new[] { huge, hugeImage, "-comp", "gzip", "-Xcompression-level", "6", "-b", "1M" }.Concat(flags).ToArray());

            var version = ToolVersion();
            if (version == FixtureToolVersion)
            {
                Assert.Equal(Embedded("extended-inodes.sqsh"), File.ReadAllBytes(extImage));
                Assert.Equal(Embedded("huge-sparse.sqsh"), File.ReadAllBytes(hugeImage));
            }

            AssertSameContent("extended-inodes.sqsh", extImage, version);
            AssertSameContent("huge-sparse.sqsh", hugeImage, version);
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }

    private static void Write(Stream stream, string ascii)
    {
        var bytes = Encoding.ASCII.GetBytes(ascii);
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// The rebuilt image holds the same files with the same content as the embedded one: names, sizes, and bytes
    /// (a few ranges of a file too large to read whole).
    /// </summary>
    private void AssertSameContent(string embeddedName, string rebuiltPath, string toolVersion)
    {
        using var embedded = new SquashFileSystemReader(new MemoryStream(Embedded(embeddedName)));
        using var rebuiltStream = File.OpenRead(rebuiltPath);
        using var rebuilt = new SquashFileSystemReader(rebuiltStream);
        var expectedFiles = embedded.GetFiles("", "*", SearchOption.AllDirectories).OrderBy(f => f).ToArray();
        var actualFiles = rebuilt.GetFiles("", "*", SearchOption.AllDirectories).OrderBy(f => f).ToArray();
        Assert.True(expectedFiles.SequenceEqual(actualFiles), $"{embeddedName} rebuilt by mksquashfs {toolVersion} lists different files");
        foreach (var file in expectedFiles)
        {
            var length = embedded.GetFileLength(file);
            Assert.True(length == rebuilt.GetFileLength(file), $"{embeddedName}: {file} rebuilt by mksquashfs {toolVersion} has another size");
            using var expected = embedded.OpenFile(file, FileMode.Open, FileAccess.Read);
            using var actual = rebuilt.OpenFile(file, FileMode.Open, FileAccess.Read);
            var ranges = length <= 8 * 1024 * 1024
                ? new (long Offset, int Count)[] { (0L, (int)length) }
                : new (long Offset, int Count)[] { (0L, 65536), (length / 2 - 32768, 65536), (length - 65536, 65536) };
            foreach (var (offset, count) in ranges)
            {
                Assert.Equal(ReadRange(expected, offset, count), ReadRange(actual, offset, count));
            }
        }
    }

    private static byte[] ReadRange(Stream stream, long offset, int count)
    {
        var buffer = new byte[count];
        stream.Position = offset;
        var total = 0;
        int read;
        while (total < count && (read = stream.Read(buffer, total, count - total)) > 0)
        {
            total += read;
        }
        Assert.Equal(count, total);
        return buffer;
    }

    private static string ToolVersion()
    {
        using var process = Process.Start(new ProcessStartInfo("mksquashfs", "-version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })
            ?? throw new InvalidOperationException("Could not start mksquashfs");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        // "mksquashfs version 4.7.5 (2026/03/01)"
        var words = output.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var index = Array.IndexOf(words, "version");
        return index >= 0 && index + 1 < words.Length ? words[index + 1] : "unknown";
    }

    private static string Text(int length)
    {
        var text = new StringBuilder();
        while (text.Length < length)
        {
            text.Append(TextLine);
        }
        return text.ToString(0, length);
    }

    private byte[] Embedded(string name)
    {
        using var stream = GetType().Assembly.GetManifestResourceStream(typeof(SquashFileSystemReaderTest), name)
            ?? throw new InvalidOperationException($"Missing test resource {name}");
        var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static bool ToolExists(string tool)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("which", tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            process!.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void Run(string tool, params string[] args)
    {
        // quoted by hand: ProcessStartInfo.ArgumentList is not in .NET Framework
        var arguments = string.Join(" ", args.Select(a => "\"" + a.Replace("\"", "\\\"") + "\""));
        var info = new ProcessStartInfo(tool, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {tool}");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{tool} failed ({process.ExitCode}): {error}");
        }
    }
}
