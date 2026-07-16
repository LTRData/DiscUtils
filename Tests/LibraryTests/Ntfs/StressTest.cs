using DiscUtils;
using DiscUtils.Ntfs;
using DiscUtils.Partitions;
using DiscUtils.Streams;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LibraryTests.Ntfs;

public class StressTest
{

    // This test is disabled by default because it takes a long time to run and is intended for stress testing scenarios.

#if LONG_RUNNING_TESTS

    [Fact]
    public async Task MftFastGrowTest()
    {
#if false
        using var containerStream = new SparseMemoryStream();
#else
        using var containerStream = new FileStream(Path.Combine(Path.GetTempPath(), "mft-overflow-repro.vhdx"),
                                                   FileMode.Create,
                                                   FileAccess.ReadWrite,
                                                   FileShare.Delete,
                                                   bufferSize: 64,
                                                   FileOptions.Asynchronous);
#endif

        using VirtualDisk destDisk = DiscUtils.Vhdx.Disk.InitializeDynamic(containerStream, Ownership.None, 100L * 1024 * 1024 * 1024);

        GuidPartitionTable.Initialize(destDisk, WellKnownPartitionType.WindowsNtfs);

        var volumeManager = new VolumeManager(destDisk);

        var logicalVolumes = volumeManager.GetLogicalVolumes();

        var targetVolume = logicalVolumes[1];

        const int testLimit = 14_000_000;

        using var destNtfs = NtfsFileSystem.Format(targetVolume, label: "Test", options: new NtfsFormatOptions());
        
        destNtfs.NtfsOptions.ShortNameCreation = ShortFileNameOption.Disabled;

        int startingFolder = 0;

        for (int i = 0; i < testLimit; i++)
        {
            if (i % 1000 == 0)
            {
                startingFolder++;
                destNtfs.CreateDirectory(startingFolder.ToString());
            }

            try
            {
                using var dest = destNtfs.OpenFile(@$"{startingFolder}\Test{i}.txt", FileMode.Create, FileAccess.ReadWrite);

                dest.Write("Here we go!"u8);
                await dest.FlushAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error: {ex.Message}. Counter: {i:N0}");
                throw;
            }
        }

        var count = 0;

        foreach (var f in destNtfs.GetFileSystemEntries("", "*", SearchOption.AllDirectories))
        {
            count++;
        }
    }

#endif

    }
