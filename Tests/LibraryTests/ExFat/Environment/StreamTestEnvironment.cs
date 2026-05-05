// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using System.IO;
using System.IO.Compression;
using DiscUtils;
using DiscUtils.Streams;
using DiscUtils.Vhdx;

namespace LibraryTests.ExFat.Environment;

internal class StreamTestEnvironment : TestEnvironment
{
    public Stream? PartitionStream { get; private set; }

    public static StreamTestEnvironment FromExistingVhdx(bool allowDebugKeep = false)
    {
        var testEnvironment = new StreamTestEnvironment();
        testEnvironment.ExtractVhdx(allowDebugKeep);
        return testEnvironment;
    }

    public override void Dispose()
    {
        PartitionStream?.Dispose();
        base.Dispose();
    }

    private void ExtractVhdx(bool allowDebugKeep)
    {
        vhdxPath = Path.Combine(Path.GetTempPath(), $"exFAT test (to be removed) {Guid.NewGuid():N}.vhdx");

        using var gzStream = GetType().Assembly.GetManifestResourceStream(GetType(), "exFAT.vhdx.gz")
            ?? throw new InvalidOperationException("Missing embedded exFAT.vhdx.gz");

        using var gzipStream = new GZipStream(gzStream, CompressionMode.Decompress);
        var fileOptions = allowDebugKeep ? 0 : FileOptions.DeleteOnClose;
        var vhdxStream = File.Create(vhdxPath, 1 << 20, fileOptions);
        gzipStream.CopyTo(vhdxStream);

        disk = new Disk(vhdxStream, Ownership.Dispose);
        var volume = VolumeManager.GetPhysicalVolumes(disk)[1];
        volumeId = volume.PartitionIdentity;
        PartitionStream = volume.Open();
    }
}