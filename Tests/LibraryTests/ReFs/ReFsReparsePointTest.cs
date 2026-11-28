using System;
using DiscUtils.ReFs.Entries;
using DiscUtils.ReFs.Entries.Files;

namespace LibraryTests.ReFs;

/// <summary>Reparse point rows: Microsoft tags and third-party tags, whose buffer carries the owner GUID before the data.</summary>
public class ReFsReparsePointTest
{
    private static readonly Guid Owner = new("6f1c2a3b-4d5e-4f60-8172-93a4b5c6d7e8");

    /// <summary>A row {u32 0, u32 buffer length, u32 buffer offset (12)} followed by the buffer {tag, data length, 0, data}.</summary>
    private static byte[] Row(uint tag, byte[] data)
    {
        var value = new byte[12 + 8 + data.Length];
        BitConverter.GetBytes((uint)(8 + data.Length)).CopyTo(value, 4);
        BitConverter.GetBytes(12u).CopyTo(value, 8);
        BitConverter.GetBytes(tag).CopyTo(value, 12);
        BitConverter.GetBytes((ushort)data.Length).CopyTo(value, 16);
        data.CopyTo(value, 20);
        return value;
    }

    /// <summary>A third-party row: the buffer is a REPARSE_GUID_DATA_BUFFER {tag, data length, 0, GUID, data}; the length excludes the GUID.</summary>
    private static byte[] ThirdPartyRow(uint tag, Guid owner, byte[] data)
    {
        var value = new byte[12 + 24 + data.Length];
        BitConverter.GetBytes((uint)(24 + data.Length)).CopyTo(value, 4);
        BitConverter.GetBytes(12u).CopyTo(value, 8);
        BitConverter.GetBytes(tag).CopyTo(value, 12);
        BitConverter.GetBytes((ushort)data.Length).CopyTo(value, 16);
        owner.ToByteArray().CopyTo(value, 20);
        data.CopyTo(value, 36);
        return value;
    }

    [Fact]
    public void AThirdPartyTagHasItsOwnerGuidBeforeTheData()
    {
        var point = new ReparsePoint(ThirdPartyRow(0x00001234, Owner, [1, 2, 3, 4]));

        Assert.True(point.IsThirdParty);
        Assert.Equal(Owner, point.ReparseGuid);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, point.Data);
    }

    [Fact]
    public void AThirdPartyTagIsReportedWithItsGuid()
    {
        var point = ReFsReparsePointDto.From(ReparsePoint.TryParse(ThirdPartyRow(0x20001234, Owner, [9])));

        Assert.NotNull(point);
        Assert.Equal(0x20001234u, point!.Tag);
        Assert.Equal(Owner, point.ReparseGuid);
        Assert.True(point.IsThirdParty);
        Assert.Equal(new byte[] { 9 }, point.Data);
    }

    [Fact]
    public void AMicrosoftTagIsNotThirdPartyAndHasNoGuid()
    {
        var point = new ReparsePoint(Row(0x80000017, [1, 2, 3, 4]));
        var dto = ReFsReparsePointDto.From(point)!;

        Assert.False(point.IsThirdParty);
        Assert.Null(point.ReparseGuid);
        Assert.False(dto.IsThirdParty);
        Assert.Null(dto.ReparseGuid);
        Assert.False(new ReFsReparsePointDto(ReFsReparsePointDto.SymbolicLinkTag, null, null, false).IsThirdParty);
    }

    [Fact]
    public void AThirdPartyBufferWithoutRoomForItsGuidIsMalformed()
    {
        Assert.Null(ReparsePoint.TryParse(Row(0x00001234, [1, 2, 3, 4]))); // only an 8-byte header and 4 bytes

        var row = ThirdPartyRow(0x00001234, Owner, [1, 2, 3, 4]);
        BitConverter.GetBytes((ushort)5).CopyTo(row, 16); // data longer than what follows the GUID
        Assert.Null(ReparsePoint.TryParse(row));
    }
}
