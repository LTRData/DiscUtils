using System;
using System.IO;
using DiscUtils;
using DiscUtils.ReFs;

namespace LibraryTests.ReFs;

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