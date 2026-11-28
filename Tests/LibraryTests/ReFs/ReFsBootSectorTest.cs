using System;
using System.IO;
using DiscUtils;
using DiscUtils.ReFs;

namespace LibraryTests.ReFs;

public class ReFsBootSectorTest
{
    private static byte[] ValidBootSector()
    {
        var data = new byte[8192];
        "ReFS"u8.CopyTo(data.AsSpan(3));
        BitConverter.GetBytes((ushort)0x58).CopyTo(data, 0x14);
        BitConverter.GetBytes(512u).CopyTo(data, 0x20);
        BitConverter.GetBytes(8u).CopyTo(data, 0x24);
        return data;
    }

    [Fact]
    public void DetectsValidBootSector()
    {
        using var stream = new MemoryStream(ValidBootSector());
        Assert.True(ReFsBootSector.Detect(stream));
    }

    [Fact]
    public void RejectsUnrelatedAndTruncatedStreams()
    {
        using var zeros = new MemoryStream(new byte[8192]);
        Assert.False(ReFsBootSector.Detect(zeros));

        using var truncated = new MemoryStream(ValidBootSector(), 0, 64);
        Assert.False(ReFsBootSector.Detect(truncated));
    }

    [Fact]
    public void RejectsBadGeometry()
    {
        var data = ValidBootSector();
        BitConverter.GetBytes(3u).CopyTo(data, 0x24);
        using var stream = new MemoryStream(data);
        Assert.False(ReFsBootSector.Detect(stream));
    }
}

public class ReFsFileSystemTest
{
    [Fact]
    public void BootSectorOnlyVolumeIsRejectedOnOpen()
    {
        var data = new byte[1 << 20];
        "ReFS"u8.CopyTo(data.AsSpan(3));
        BitConverter.GetBytes((ushort)0x58).CopyTo(data, 0x14);
        BitConverter.GetBytes((ulong)data.Length).CopyTo(data, 0x18);
        BitConverter.GetBytes(512u).CopyTo(data, 0x20);
        BitConverter.GetBytes(8u).CopyTo(data, 0x24);

        // Detection recognises the signature, but opening validates the checksum and structures.
        Assert.ThrowsAny<InvalidFileSystemException>(() => new ReFsFileSystem(new MemoryStream(data)));
    }

    [Fact]
    public void RejectsWrongSignature()
    {
        Assert.Throws<InvalidFileSystemException>(() => new ReFsFileSystem(new MemoryStream(new byte[8192])));
    }
}
