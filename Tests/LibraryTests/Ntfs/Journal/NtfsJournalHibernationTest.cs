using System;
using System.IO;
using System.Linq;
using System.Text;
using DiscUtils.Ntfs;

namespace LibraryTests.Ntfs.Journal;

/// <summary>
/// A hibernated Windows (also Fast Startup) leaves its journals open like a crash, but resumes with the metadata it
/// kept in memory. In the lab, a data volume that DiscUtils replayed and wrote while its Windows was hibernated came
/// back without the new file, and chkdsk then found errors to repair offline. hiberfil.sys starts with "HIBR" while
/// there is an image to resume, and with "WAKE" once Windows resumed from it.
/// </summary>
public class NtfsJournalHibernationTest
{
    /// <summary>A crashed volume (its journal holds the creation of "dir") with a hiberfil.sys that starts with <paramref name="header"/>.</summary>
    private static MemoryStream CrashedVolumeWithHiberfil(string header)
    {
        var scenario = JournalScenario.Create(ntfs => ntfs.CreateDirectory(@"dir\sub"), setup: ntfs =>
        {
            var content = new byte[8192];
            Encoding.ASCII.GetBytes(header).CopyTo(content, 0);
            using var file = ntfs.OpenFile("hiberfil.sys", FileMode.Create, FileAccess.Write);
            file.Write(content, 0, content.Length);
        });
        var volume = JournalScenario.Copy(scenario.Before);
        scenario.WriteJournal(volume, committed: true);
        return volume;
    }

    [Theory]
    [InlineData("HIBR")]
    [InlineData("hibr")]
    public void AVolumeWithAnImageToResume_IsNeverReplayedOrOpenedForWriting(string header)
    {
        using var volume = CrashedVolumeWithHiberfil(header);
        var image = volume.ToArray();

        var info = NtfsJournal.Analyze(volume);
        Assert.Equal(NtfsJournalStatus.Unsupported, info.Status);
        Assert.Contains("hibernated", info.Detail);

        Assert.Contains("hibernated", Assert.Throws<IOException>(() => NtfsJournal.Replay(volume)).Message);
        Assert.Throws<IOException>(() => new NtfsFileSystem(volume, NtfsJournalMode.Replay));
        Assert.Throws<IOException>(() => new NtfsFileSystem(volume));
        Assert.True(image.AsSpan().SequenceEqual(volume.ToArray()), "nothing may be written");

        // Reading shows the volume as it is on disk.
        using (var ntfs = new NtfsFileSystem(new MemoryStream(image, writable: false), NtfsJournalMode.Replay))
        {
            Assert.Equal(NtfsJournalStatus.Unsupported, ntfs.JournalInfo.Status);
            Assert.False(ntfs.JournalInfo.ReplayedInMemory);
            Assert.False(ntfs.DirectoryExists("dir"));
        }

        // Ignore stays the caller's explicit choice.
        using (var ntfs = new NtfsFileSystem(volume, NtfsJournalMode.Ignore))
        {
            Assert.Null(ntfs.JournalInfo);
        }
    }

    [Theory]
    [InlineData("WAKE")]
    [InlineData("\0\0\0\0")]
    public void AHiberfilWithoutAnImageToResume_DoesNotStopTheReplay(string header)
    {
        using var volume = CrashedVolumeWithHiberfil(header);
        Assert.Equal(NtfsJournalStatus.NeedsReplay, NtfsJournal.Analyze(volume).Status);

        using var ntfs = new NtfsFileSystem(volume, NtfsJournalMode.Replay);
        Assert.True(ntfs.JournalInfo.Replayed);
        Assert.True(ntfs.DirectoryExists(@"dir\sub"));
    }
}
