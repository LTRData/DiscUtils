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
using DiscUtils.Streams;

namespace DiscUtils.Archives;

public sealed class TarHeader
{
    public const int Length = 512;

    public string FileName { get; internal set; }
    public UnixFilePermissions FileMode { get; }
    public int OwnerId { get; }
    public int GroupId { get; }
    public long FileLength { get; }
    public DateTimeOffset ModificationTime { get; }
    public int CheckSum { get; }
    public UnixFileType FileType { get; }
    public string LinkName { get; }
    public string Magic { get; }
    public int Version { get; }
    public string OwnerName { get; }
    public string GroupName { get; }
    public int DevMajor { get; }
    public int DevMinor { get; }
    public DateTimeOffset LastAccessTime { get; }
    public DateTimeOffset CreationTime { get; }

    public TarHeader()
    {
    }

    public TarHeader(string fileName, long fileLength, UnixFilePermissions fileMode, int ownerId, int groupId, DateTime modificationTime)
    {
        FileName = fileName;
        FileLength = fileLength;
        FileMode = fileMode;
        OwnerId = ownerId;
        GroupId = groupId;
        ModificationTime = modificationTime;
    }

    public TarHeader(byte[] buffer, int offset)
        : this(buffer.AsSpan(offset))
    {
    }

    public TarHeader(ReadOnlySpan<byte> buffer)
    {
        var latin1Encoding = EncodingUtilities.GetLatin1Encoding();

        FileName = latin1Encoding.GetString(ReadNullTerminatedString(buffer.Slice(0, 100)));
        FileMode = (UnixFilePermissions)OctalToLong(ReadNullTerminatedString(buffer.Slice(100, 8)));
        OwnerId = (int)OctalToLong(ReadNullTerminatedString(buffer.Slice(108, 8)));
        GroupId = (int)OctalToLong(ReadNullTerminatedString(buffer.Slice(116, 8)));
        FileLength = ParseFileLength(buffer.Slice(124, 12));
        ModificationTime = DateTimeOffset.FromUnixTimeSeconds((uint)OctalToLong(ReadNullTerminatedString(buffer.Slice(136, 12))));
        CheckSum = (int)OctalToLong(ReadNullTerminatedString(buffer.Slice(148, 8)));
        FileType = (UnixFileType)buffer[156];
        LinkName = latin1Encoding.GetString(ReadNullTerminatedString(buffer.Slice(157, 100)));
        Magic = latin1Encoding.GetString(ReadNullTerminatedString(buffer.Slice(257, 6)));
        Version = (int)OctalToLong(ReadNullTerminatedString(buffer.Slice(263, 2)));
        OwnerName = latin1Encoding.GetString(ReadNullTerminatedString(buffer.Slice(265, 32)));
        GroupName = latin1Encoding.GetString(ReadNullTerminatedString(buffer.Slice(297, 32)));
        DevMajor = (int)OctalToLong(ReadNullTerminatedString(buffer.Slice(329, 8)));
        DevMinor = (int)OctalToLong(ReadNullTerminatedString(buffer.Slice(337, 8)));

        var prefix = ReadNullTerminatedString(buffer.Slice(345, 131));

        if (!prefix.IsEmpty)
        {
            FileName = $"{latin1Encoding.GetString(prefix)}/{FileName}";
        }

        LastAccessTime = DateTimeOffset.FromUnixTimeSeconds((uint)OctalToLong(ReadNullTerminatedString(buffer.Slice(476, 12))));
        CreationTime = DateTimeOffset.FromUnixTimeSeconds((uint)OctalToLong(ReadNullTerminatedString(buffer.Slice(488, 12))));
    }

    public static bool IsValid(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Slice(0, 100).IndexOf((byte)0) > 0 &&
            IsValidOctalString(buffer.Slice(100, 8)) &&
            IsValidOctalString(buffer.Slice(108, 8)) &&
            IsValidOctalString(buffer.Slice(116, 8)) &&
            IsValidOctalString(buffer.Slice(136, 12)) &&
            IsValidOctalString(buffer.Slice(148, 8)) &&
            buffer.Slice(257, 6).IndexOf((byte)0) > 0 &&
            IsValidOctalString(buffer.Slice(263, 2)) &&
            buffer.Slice(265, 32).IndexOf((byte)0) >= 0 &&
            buffer.Slice(297, 32).IndexOf((byte)0) >= 0 &&
            IsValidOctalString(buffer.Slice(329, 8)) &&
            IsValidOctalString(buffer.Slice(337, 8)) &&
            IsValidOctalString(buffer.Slice(476, 12)) &&
            IsValidOctalString(buffer.Slice(488, 12)))
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    private static bool IsValidOctalString(ReadOnlySpan<byte> v)
    {
        v = ReadNullTerminatedString(v);

        foreach (var c in v)
        {
            if (c is < (byte)'0' or > (byte)'9')
            {
                return false;
            }
        }

        return true;
    }

    private static long ParseFileLength(ReadOnlySpan<byte> buffer)
    {
        if (EndianUtilities.ToInt32LittleEndian(buffer) == 128)
        {
            return EndianUtilities.ToInt64BigEndian(buffer.Slice(4));
        }
        else
        {
            return OctalToLong(ReadNullTerminatedString(buffer));
        }
    }

    public void WriteTo(Span<byte> buffer)
    {
        buffer.Clear();

        var latin1Encoding = EncodingUtilities.GetLatin1Encoding();

        if (FileName.Length < 100)
        {
            latin1Encoding.GetBytes(FileName, buffer.Slice(0, 99));
        }
        else
        {
            var split = FileName.LastIndexOf('/', Math.Min(130, FileName.Length - 2));

            if (split < 0 || FileName.Length - split > 99)
            {
                throw new InvalidOperationException($"File name '{FileName}' too long for tar header");
            }

            var prefix = FileName.AsSpan(0, split);
            latin1Encoding.GetBytes(prefix, buffer.Slice(345, 130));

            var filename = FileName.AsSpan(split + 1);
            latin1Encoding.GetBytes(filename, buffer.Slice(0, 99));
        }

        latin1Encoding.GetBytes(LongToOctal((long)FileMode, 7), buffer.Slice(100, 7));
        latin1Encoding.GetBytes(LongToOctal(OwnerId, 7), buffer.Slice(108, 7));
        latin1Encoding.GetBytes(LongToOctal(GroupId, 7), buffer.Slice(116, 7));
        latin1Encoding.GetBytes(LongToOctal(FileLength, 11), buffer.Slice(124, 11));
        latin1Encoding.GetBytes(LongToOctal(ModificationTime.ToUnixTimeSeconds(), 11), buffer.Slice(136, 11));

        buffer.Slice(148, 8).Fill((byte)' ');

        // Checksum

        long checkSum = 0;
        for (var i = 0; i < 512; ++i)
        {
            checkSum += buffer[i];
        }

        latin1Encoding.GetBytes(LongToOctal(checkSum, 7), buffer.Slice(148, 7));
        buffer[155] = 0;
    }

    internal static ReadOnlySpan<byte> ReadNullTerminatedString(ReadOnlySpan<byte> v)
    {
        while (v.Length > 0 && v[0] == ' ')
        {
            v = v.Slice(1);
        }

        var z = v.IndexOf((byte)0);
        if (z >= 0)
        {
            v = v.Slice(0, z);
        }

        while (v.Length > 0 && v[v.Length - 1] == ' ')
        {
            v = v.Slice(0, v.Length - 1);
        }

        return v;
    }

    private static long OctalToLong(ReadOnlySpan<byte> value)
    {
        long result = 0;

        for (var i = 0; i < value.Length; ++i)
        {
            result = (result * 8) + (value[i] - '0');
        }

        return result;
    }

    private static string LongToOctal(long value, int length) =>
        Convert.ToString(value, 8).PadLeft(length, '0');
    //{
    //    string result = string.Empty;

    //    while (value > 0)
    //    {
    //        result = ((char)('0' + (value % 8))) + result;
    //        value = value / 8;
    //    }

    //    return new string('0', length - result.Length) + result;
    //}

    public override string ToString() => FileName;
}
