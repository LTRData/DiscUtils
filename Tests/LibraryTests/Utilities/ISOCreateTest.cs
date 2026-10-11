// SPDX-License-Identifier: MIT
using System;
using System.IO;
using DiscUtils.Iso9660;
using DiscUtils.Udf;
using Xunit;

namespace LibraryTests.Utilities;

[Collection("ISOCreate command line")]
public class ISOCreateTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultBuildsIsoWithExistingDirectoryBehavior(bool trailingSeparator)
    {
        using var tree = new SourceTree();
        // The filename extension must not select UDF mode.
        var output = Path.Combine(tree.Root, "default.udf");
        new TestProgram().Execute("-q", "-vl", "ISO_TEST", output, tree.SourcePath(trailingSeparator));

        using var image = File.OpenRead(output);
        Assert.True(CDReader.Detect(image));
        Assert.False(UdfReader.Detect(image));
        using var isoReader = new CDReader(image, false);
        Assert.Equal("ISO_TEST", isoReader.VolumeLabel);
        using var reader = new CDReader(image, true);
        Assert.False(reader.HasBootImage);
        Assert.Equal("hello", Helpers.Helpers.GetFileContent("hello.txt", reader));
        Assert.Equal("nested", Helpers.Helpers.GetFileContent("folder\\child.txt", reader));
        Assert.Equal("unicode", Helpers.Helpers.GetFileContent("字幕\\繁體中文.txt", reader));
        Assert.Equal(0, reader.GetFileLength("zero.txt"));
        Assert.False(reader.DirectoryExists("empty"));
        Assert.False(reader.DirectoryExists("folder\\empty"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UdfSwitchPreservesFilesNamesAndEmptyDirectories(bool trailingSeparator)
    {
        using var tree = new SourceTree();
        var output = Path.Combine(tree.Root, "udf.iso");
        new TestProgram().Execute("-q", "-udf", "-vollabel", "UDF_TEST", output, tree.SourcePath(trailingSeparator));

        using var image = File.OpenRead(output);
        Assert.True(UdfReader.Detect(image));
        Assert.False(CDReader.Detect(image));
        using var reader = new UdfReader(image);
        Assert.Equal("UDF_TEST", reader.VolumeLabel);
        Assert.Equal("hello", Helpers.Helpers.GetFileContent("hello.txt", reader));
        Assert.Equal("nested", Helpers.Helpers.GetFileContent("folder\\child.txt", reader));
        Assert.Equal("unicode", Helpers.Helpers.GetFileContent("字幕\\繁體中文.txt", reader));
        Assert.Equal(0, reader.GetFileLength("zero.txt"));
        Assert.True(reader.DirectoryExists("empty"));
        Assert.True(reader.DirectoryExists("folder\\empty"));
    }

    [Fact]
    public void UdfSwitchWorksWithoutVolumeLabel()
    {
        using var tree = new SourceTree();
        var output = Path.Combine(tree.Root, "udf.iso");
        new TestProgram().Execute("-q", output, tree.Source, "-udf");

        using var image = File.OpenRead(output);
        using var reader = new UdfReader(image);
        Assert.Equal("DISC", reader.VolumeLabel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UdfBootImageFailsBeforeOpeningOrOverwritingOutput(bool existingOutput)
    {
        using var tree = new SourceTree();
        var output = Path.Combine(tree.Root, "udf.iso");
        var original = new byte[] { 1, 2, 3 };
        if (existingOutput)
        {
            File.WriteAllBytes(output, original);
        }

        var previousExitCode = Environment.ExitCode;
        try
        {
            Environment.ExitCode = 0;
            new TestProgram().Execute("-q", "-udf", output, tree.Source, Path.Combine(tree.Root, "missing-boot.img"));
            Assert.Equal(1, Environment.ExitCode);
            if (existingOutput)
            {
                Assert.Equal(original, File.ReadAllBytes(output));
            }
            else
            {
                Assert.False(File.Exists(output));
            }
        }
        finally
        {
            Environment.ExitCode = previousExitCode;
        }
    }

    private sealed class TestProgram : ISOCreate.Program
    {
        public void Execute(params string[] args) => Run(args);
    }

    private sealed class SourceTree : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "isocreate-" + Guid.NewGuid().ToString("N"));
        public string Source { get; }

        public SourceTree()
        {
            Source = Path.Combine(Root, "source");
            Directory.CreateDirectory(Path.Combine(Source, "empty"));
            Directory.CreateDirectory(Path.Combine(Source, "folder", "empty"));
            Directory.CreateDirectory(Path.Combine(Source, "字幕"));
            File.WriteAllText(Path.Combine(Source, "hello.txt"), "hello");
            File.WriteAllText(Path.Combine(Source, "folder", "child.txt"), "nested");
            File.WriteAllText(Path.Combine(Source, "字幕", "繁體中文.txt"), "unicode");
            File.WriteAllBytes(Path.Combine(Source, "zero.txt"), new byte[0]);
        }

        public string SourcePath(bool trailingSeparator) => trailingSeparator ? Source + Path.DirectorySeparatorChar : Source;

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

// These tests temporarily set the process exit code to exercise command-line failures.
[CollectionDefinition("ISOCreate command line", DisableParallelization = true)]
public class ISOCreateCollection
{
}
