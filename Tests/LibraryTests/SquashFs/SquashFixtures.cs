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
/// The SquashFS test images. When squashfs-tools (4.5 or later) is on the machine, they are built fresh from the
/// recipe below by that mksquashfs, once per test run, and the reader tests run on those; the images embedded in the
/// assembly, made the same way with squashfs-tools 4.7.5, are only the fallback where the tool is absent. So on a
/// machine with the tool, nothing in the repository's binaries is trusted or even opened.
/// </summary>
internal static class SquashFixtures
{
    private const string TextLine = "a line of text that compresses well, over and over\n";
    private static readonly object Gate = new();
    private static string? _built;

    /// <summary>The 300,000-byte text of text.bin.</summary>
    public static string Text
    {
        get
        {
            var text = new StringBuilder();
            while (text.Length < 300000)
            {
                text.Append(TextLine);
            }
            return text.ToString(0, 300000);
        }
    }

    /// <summary>
    /// The 524,388 bytes of mixed.bin: with 128 KB blocks, a block of text (compressed), a block of pseudo-random
    /// bytes (incompressible, so mksquashfs stores it as is), a block of zeros (sparse), another block of text and
    /// a 100-byte tail (a fragment).
    /// </summary>
    public static byte[] Mixed
    {
        get
        {
            const int block = 128 * 1024;
            var bytes = new byte[4 * block + 100];
            var text = Encoding.ASCII.GetBytes(Text);
            Array.Copy(text, 0, bytes, 0, block);
            var x = 0x9E3779B9u; // xorshift32: the same bytes on every machine
            for (var i = block; i < 2 * block; i++)
            {
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                bytes[i] = (byte)x;
            }

            Array.Copy(text, 0, bytes, 3 * block, block);
            for (var i = 4 * block; i < bytes.Length; i++)
            {
                bytes[i] = (byte)('a' + i % 26);
            }

            return bytes;
        }
    }

    /// <summary>A seekable copy of an image: freshly built when mksquashfs is available, else the embedded one.</summary>
    public static Stream Open(string name)
    {
        var built = Built();
        var image = new MemoryStream();
        if (built is not null)
        {
            using var file = File.OpenRead(Path.Combine(built, name));
            file.CopyTo(image);
        }
        else
        {
            using var resource = typeof(SquashFixtures).Assembly.GetManifestResourceStream(typeof(SquashFileSystemReaderTest), name)
                ?? throw new InvalidOperationException($"Missing test resource {name}");
            resource.CopyTo(image);
        }
        image.Position = 0;
        return image;
    }

    /// <summary>The folder holding the freshly built images, or null when they cannot be built here.</summary>
    private static string? Built()
    {
        lock (Gate)
        {
            if (_built is not null)
            {
                return _built.Length == 0 ? null : _built;
            }
            if (Environment.OSVersion.Platform != PlatformID.Unix || !ToolExists("mksquashfs") || !ToolExists("ln"))
            {
                _built = "";
                return null;
            }
            var work = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "squashfs-fixtures-" + Path.GetRandomFileName()));
            Build(work.FullName);
            _built = work.FullName;
            return _built;
        }
    }

    /// <summary>
    /// The recipe. extended-inodes.sqsh: 128 KB blocks; small.txt ("hello", a fragment), text.bin (300,000 bytes,
    /// two stored blocks and a fragment), sparse.bin (2 MiB of zeros then "end": sparse blocks, an extended inode),
    /// hard.txt and dir/hard2.txt (one file, two names: an extended inode), mixed.bin (<see cref="Mixed"/>: a
    /// compressed, a stored, a sparse and a compressed block, then a fragment). huge-sparse.sqsh: 1 MiB blocks;
    /// huge.bin, 4 GiB + 4 bytes, "mid" at 2 GiB + 5 and "end!" past the 4 GiB mark, nearly all of it sparse.
    /// Both gzip at level 6, so the superblock carries compressor options. big-root.sqsh: 1,000 files entry-0000
    /// ("first") to entry-0999 (empty) in the root, whose directory table then exceeds one metadata block, so
    /// mksquashfs stores the root as an extended directory inode with an index; and link, a symbolic link to
    /// entry-0000. xattr-symlink.sqsh: target.txt ("hello") and link, a symbolic link to it, built with
    /// -xattrs-add, which puts an attribute on every inode: the link is an extended symlink inode and the image
    /// has an xattr table. The other three are built with -no-xattrs.
    /// </summary>
    private static void Build(string work)
    {
        var ext = Path.Combine(work, "ext");
        Directory.CreateDirectory(Path.Combine(ext, "dir"));
        File.WriteAllText(Path.Combine(ext, "small.txt"), "hello", Encoding.ASCII);
        File.WriteAllText(Path.Combine(ext, "text.bin"), Text, Encoding.ASCII);
        using (var sparse = File.Create(Path.Combine(ext, "sparse.bin")))
        {
            sparse.SetLength(2L * 1024 * 1024);
            sparse.Position = sparse.Length;
            Write(sparse, "end");
        }
        File.WriteAllText(Path.Combine(ext, "hard.txt"), "twice", Encoding.ASCII);
        Run("ln", Path.Combine(ext, "hard.txt"), Path.Combine(ext, "dir", "hard2.txt"));
        File.WriteAllBytes(Path.Combine(ext, "mixed.bin"), Mixed);

        var huge = Path.Combine(work, "huge");
        Directory.CreateDirectory(huge);
        using (var file = File.Create(Path.Combine(huge, "huge.bin")))
        {
            file.SetLength(4L * 1024 * 1024 * 1024); // sparse on disk
            file.Position = 2L * 1024 * 1024 * 1024 + 5;
            Write(file, "mid");
            file.Position = file.Length;
            Write(file, "end!");
        }

        var bigRoot = Path.Combine(work, "big-root");
        Directory.CreateDirectory(bigRoot);
        for (var i = 0; i < 1000; i++)
        {
            File.WriteAllText(Path.Combine(bigRoot, $"entry-{i:D4}"), i == 0 ? "first" : "", Encoding.ASCII);
        }
        Run("ln", "-s", "entry-0000", Path.Combine(bigRoot, "link"));

        var xattrSymlink = Path.Combine(work, "xattr-symlink");
        Directory.CreateDirectory(xattrSymlink);
        File.WriteAllText(Path.Combine(xattrSymlink, "target.txt"), "hello", Encoding.ASCII);
        Run("ln", "-s", "target.txt", Path.Combine(xattrSymlink, "link"));

        var flags = new[] { "-noappend", "-no-progress", "-quiet", "-mkfs-time", "0", "-all-time", "0", "-force-uid", "0", "-force-gid", "0" };
        var noXattrs = new[] { "-no-xattrs" }.Concat(flags).ToArray();
        Run("mksquashfs", new[] { ext, Path.Combine(work, "extended-inodes.sqsh"), "-comp", "gzip", "-Xcompression-level", "6", "-b", "128K" }.Concat(noXattrs).ToArray());
        Run("mksquashfs", new[] { huge, Path.Combine(work, "huge-sparse.sqsh"), "-comp", "gzip", "-Xcompression-level", "6", "-b", "1M" }.Concat(noXattrs).ToArray());
        Run("mksquashfs", new[] { bigRoot, Path.Combine(work, "big-root.sqsh"), "-comp", "gzip", "-b", "128K" }.Concat(noXattrs).ToArray());
        Run("mksquashfs", new[] { xattrSymlink, Path.Combine(work, "xattr-symlink.sqsh"), "-comp", "gzip", "-b", "128K", "-xattrs-add", "trusted.test=1" }.Concat(flags).ToArray());
        Directory.Delete(ext, recursive: true);
        Directory.Delete(huge, recursive: true);
        Directory.Delete(bigRoot, recursive: true);
        Directory.Delete(xattrSymlink, recursive: true);
    }

    private static void Write(Stream stream, string ascii)
    {
        var bytes = Encoding.ASCII.GetBytes(ascii);
        stream.Write(bytes, 0, bytes.Length);
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
