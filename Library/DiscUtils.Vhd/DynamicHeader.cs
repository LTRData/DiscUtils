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
using System.IO;
using System.Text;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Vhd;

internal class DynamicHeader
{
    public const string HeaderCookie = "cxsparse";
    public const uint Version1 = 0x00010000;
    public const uint DefaultBlockSize = 0x00200000;
    public uint BlockSize;
    public uint Checksum;

    public string Cookie;
    public long DataOffset;
    public uint HeaderVersion;
    public int MaxTableEntries;
    public ParentLocator[] ParentLocators;
    public DateTime ParentTimestamp;
    public string ParentUnicodeName;
    public Guid ParentUniqueId;
    public long TableOffset;

    public DynamicHeader() {}

    public DynamicHeader(long dataOffset, long tableOffset, uint blockSize, long diskSize)
    {
        Cookie = HeaderCookie;
        DataOffset = dataOffset;
        TableOffset = tableOffset;
        HeaderVersion = Version1;
        BlockSize = blockSize;
        MaxTableEntries = (int)((diskSize + blockSize - 1) / blockSize);
        ParentTimestamp = Footer.EpochUtc;
        ParentUnicodeName = string.Empty;
        ParentLocators = new ParentLocator[8];
        for (var i = 0; i < 8; ++i)
        {
            ParentLocators[i] = new ParentLocator();
        }
    }

    public DynamicHeader(DynamicHeader toCopy)
    {
        Cookie = toCopy.Cookie;
        DataOffset = toCopy.DataOffset;
        TableOffset = toCopy.TableOffset;
        HeaderVersion = toCopy.HeaderVersion;
        MaxTableEntries = toCopy.MaxTableEntries;
        BlockSize = toCopy.BlockSize;
        Checksum = toCopy.Checksum;
        ParentUniqueId = toCopy.ParentUniqueId;
        ParentTimestamp = toCopy.ParentTimestamp;
        ParentUnicodeName = toCopy.ParentUnicodeName;
        ParentLocators = new ParentLocator[toCopy.ParentLocators.Length];
        for (var i = 0; i < ParentLocators.Length; ++i)
        {
            ParentLocators[i] = new ParentLocator(toCopy.ParentLocators[i]);
        }
    }

    public static DynamicHeader FromBytes(ReadOnlySpan<byte> data)
    {
        var latin1Encoding = EncodingUtilities.GetLatin1Encoding();

        var result = new DynamicHeader
        {
            Cookie = latin1Encoding.GetString(data.Slice(0, 8)),
            DataOffset = EndianUtilities.ToInt64BigEndian(data.Slice(8)),
            TableOffset = EndianUtilities.ToInt64BigEndian(data.Slice(16)),
            HeaderVersion = EndianUtilities.ToUInt32BigEndian(data.Slice(24)),
            MaxTableEntries = EndianUtilities.ToInt32BigEndian(data.Slice(28)),
            BlockSize = EndianUtilities.ToUInt32BigEndian(data.Slice(32)),
            Checksum = EndianUtilities.ToUInt32BigEndian(data.Slice(36)),
            ParentUniqueId = EndianUtilities.ToGuidBigEndian(data.Slice(40)),
            ParentTimestamp = Footer.EpochUtc.AddSeconds(EndianUtilities.ToUInt32BigEndian(data.Slice(56))),
            ParentUnicodeName = Encoding.BigEndianUnicode.GetString(data.Slice(64, 512)).TrimEnd('\0'),
            ParentLocators = new ParentLocator[8]
        };

        for (var i = 0; i < 8; ++i)
        {
            result.ParentLocators[i] = ParentLocator.FromBytes(data.Slice(576 + i * 24));
        }

        return result;
    }

    public void ToBytes(Span<byte> data)
    {
        var latin1Encoding = EncodingUtilities.GetLatin1Encoding();

        latin1Encoding.GetBytes(Cookie.AsSpan(), data.Slice(0, 8));
        EndianUtilities.WriteBytesBigEndian(DataOffset, data.Slice(8));
        EndianUtilities.WriteBytesBigEndian(TableOffset, data.Slice(16));
        EndianUtilities.WriteBytesBigEndian(HeaderVersion, data.Slice(24));
        EndianUtilities.WriteBytesBigEndian(MaxTableEntries, data.Slice(28));
        EndianUtilities.WriteBytesBigEndian(BlockSize, data.Slice(32));
        EndianUtilities.WriteBytesBigEndian(Checksum, data.Slice(36));
        EndianUtilities.WriteBytesBigEndian(ParentUniqueId, data.Slice(40));
        EndianUtilities.WriteBytesBigEndian((uint)(ParentTimestamp - Footer.EpochUtc).TotalSeconds, data.Slice(56));
        EndianUtilities.WriteBytesBigEndian((uint)0, data.Slice(60));
        data.Slice(64, 512).Clear();
        Encoding.BigEndianUnicode.GetBytes(ParentUnicodeName.AsSpan(), data.Slice(64));

        for (var i = 0; i < 8; ++i)
        {
            ParentLocators[i].ToBytes(data.Slice(576 + i * 24));
        }

        data.Slice(1024 - 256, 256).Clear();
    }

    public bool IsValid()
    {
        return (Cookie == HeaderCookie)
               && IsChecksumValid()
               && HeaderVersion == Version1;
    }

    public bool IsChecksumValid()
    {
        return Checksum == CalculateChecksum();
    }

    public uint UpdateChecksum()
    {
        Checksum = CalculateChecksum();
        return Checksum;
    }

    internal static DynamicHeader FromStream(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[1024];
        stream.ReadExactly(buffer);
        return FromBytes(buffer);
    }

    private uint CalculateChecksum()
    {
        var copy = new DynamicHeader(this)
        {
            Checksum = 0
        };

        Span<byte> asBytes = stackalloc byte[1024];
        copy.ToBytes(asBytes);
        uint checksum = 0;
        foreach (uint value in asBytes)
        {
            checksum += value;
        }

        checksum = ~checksum;

        return checksum;
    }
}