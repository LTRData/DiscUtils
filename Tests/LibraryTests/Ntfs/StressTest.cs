using DiscUtils;
using DiscUtils.Ntfs;
using DiscUtils.Partitions;
using DiscUtils.Streams;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace LibraryTests.Ntfs;

public class StressTest
{
    // This test is disabled by default because it takes a long time to run and is intended for stress testing scenarios.

#if LONG_RUNNING_TESTS

    private const string imageFilename = "mft-overflow-repro.vhdx";

    const int testLimit = 10_000;

    [Fact]
    public async Task MftFastGrowTest()
    {
#if !true

        using var targetVolume = new SparseMemoryStream();

        targetVolume.SetLength(100L * 1024 * 1024 * 1024);

        var diskGeometry = Geometry.FromCapacity(targetVolume.Length);

        using var destNtfs = NtfsFileSystem.Format(targetVolume, label: "Test", diskGeometry, 0, diskGeometry.TotalSectorsLong, options: new NtfsFormatOptions());

#endif

        var baseDir = Path.GetTempPath();

        var testData = "Here we go!"u8.ToArray();

        var validationBuffer = new byte[testData.Length];

        var startTime = Environment.TickCount;

        byte[]? originchecksum = null;

        using (var containerStream = new FileStream(Path.Combine(baseDir, imageFilename),
                                                    FileMode.Create,
                                                    FileAccess.ReadWrite,
                                                    FileShare.Delete,
                                                    bufferSize: 4096,
                                                    FileOptions.Asynchronous))
        using (VirtualDisk destDisk = DiscUtils.Vhdx.Disk.InitializeDynamic(containerStream, Ownership.None, 100L * 1024 * 1024 * 1024))
        {
            GuidPartitionTable.Initialize(destDisk, WellKnownPartitionType.WindowsNtfs);

            var volumeManager = new VolumeManager(destDisk);

            var logicalVolumes = volumeManager.GetLogicalVolumes();

            var targetVolume = logicalVolumes[^1];

            using (var destNtfs = NtfsFileSystem.Format(targetVolume, label: "Test", options: new NtfsFormatOptions()))
            {
                destNtfs.NtfsOptions.ShortNameCreation = ShortFileNameOption.Disabled;
                destNtfs.NtfsOptions.HideHiddenFiles = false;
                destNtfs.NtfsOptions.HideSystemFiles = false;
                destNtfs.NtfsOptions.HideMetafiles = false;

                int startingFolder = 0;

                var count = 0L;

                for (int i = 0; i < testLimit; i++)
                {
                    count++;

                    if (i % 1000 == 0)
                    {
                        startingFolder++;
                        destNtfs.CreateDirectory(startingFolder.ToString());
                    }

                    var path = @$"{startingFolder}\Test{i}.txt";

                    if (Environment.TickCount - startTime > 500)
                    {
                        Console.Write($"Creating file {count:N0} path {path}...  \r");
                        startTime = Environment.TickCount;
                    }

                    try
                    {
                        using var dest = destNtfs.OpenFile(path, FileMode.Create, FileAccess.ReadWrite);

                        dest.Write(testData);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Error: {ex.Message}. Counter: {i:N0}");
                        throw;
                    }
                }

                Console.WriteLine();
                Console.WriteLine("Closing...");
            }

#if NET6_0_OR_GREATER
            originchecksum = MD5.HashData(targetVolume.Open());
#endif
        }

        Console.WriteLine("Reopening...");

        using (var containerStream = new FileStream(Path.Combine(baseDir, imageFilename),
                                                    FileMode.Open,
                                                    FileAccess.Read,
                                                    FileShare.Delete | FileShare.Read,
                                                    bufferSize: 4096,
                                                    FileOptions.Asynchronous))
        using (VirtualDisk destDisk = new DiscUtils.Vhdx.Disk(containerStream, Ownership.Dispose))
        {
            var volumeManager = new VolumeManager(destDisk);

            var logicalVolumes = volumeManager.GetLogicalVolumes();

            var targetVolume = logicalVolumes[^1];

#if NET6_0_OR_GREATER
            var currentchecksum = MD5.HashData(targetVolume.Open());

            Assert.Equal(originchecksum, currentchecksum);
#endif

            using (var validationNtfs = new NtfsFileSystem(targetVolume.Open()))
            {
                validationNtfs.NtfsOptions.HideHiddenFiles = false;
                validationNtfs.NtfsOptions.HideSystemFiles = false;
                validationNtfs.NtfsOptions.HideMetafiles = false;

                var count = 0L;

                foreach (var path in validationNtfs.GetFiles("", "*.txt", SearchOption.AllDirectories))
                {
                    count++;

                    if (Environment.TickCount - startTime > 500)
                    {
                        Console.Write($"Validation {count:N0}, opening file {path}...  \r");
                        startTime = Environment.TickCount;
                    }

                    try
                    {
                        using var dest = validationNtfs.OpenFile(path, FileMode.Open, FileAccess.Read);

#if NETCOREAPP || NETSTANDARD2_1_OR_GREATER
                        if (Path.GetExtension(path.AsSpan()).SequenceEqual(".txt"))
                        {
                            await dest.ReadExactlyAsync(validationBuffer);

                            Assert.Equal(testData, validationBuffer);
                        }
#endif
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Error: {ex.Message}. Path: {path}");
                        //throw;
                    }
                }

                if (count != testLimit)
                {
                    throw new FileNotFoundException($"Expected {testLimit} files but found {count}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("Done.");
    }

#endif

}
