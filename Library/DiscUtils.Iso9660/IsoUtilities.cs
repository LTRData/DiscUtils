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
using System.Globalization;
using System.IO;
using System.Text;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;
using LTRData.Extensions.Buffers;

namespace DiscUtils.Iso9660;

internal static class IsoUtilities
{
    public const int SectorSize = 2048;

    public static uint ToUInt32FromBoth(ReadOnlySpan<byte> data)
    {
        return EndianUtilities.ToUInt32LittleEndian(data);
    }

    public static ushort ToUInt16FromBoth(ReadOnlySpan<byte> data)
    {
        return EndianUtilities.ToUInt16LittleEndian(data);
    }

    internal static void ToBothFromUInt32(Span<byte> buffer, uint value)
    {
        EndianUtilities.WriteBytesLittleEndian(value, buffer);
        EndianUtilities.WriteBytesBigEndian(value, buffer.Slice(4));
    }

    internal static void ToBothFromUInt16(Span<byte> buffer, ushort value)
    {
        EndianUtilities.WriteBytesLittleEndian(value, buffer);
        EndianUtilities.WriteBytesBigEndian(value, buffer.Slice(2));
    }

    internal static void ToBytesFromUInt32(Span<byte> buffer, uint value)
    {
        EndianUtilities.WriteBytesLittleEndian(value, buffer);
    }

    internal static void ToBytesFromUInt16(Span<byte> buffer, ushort value)
    {
        EndianUtilities.WriteBytesLittleEndian(value, buffer);
    }

    internal static void WriteAChars(Span<byte> buffer, string str)
    {
        // Validate string
        if (!IsValidAString(str))
        {
            throw new IOException("Attempt to write string with invalid a-characters");
        }

        ////WriteASCII(buffer, offset, numBytes, true, str);
        WriteString(buffer, true, str, Encoding.ASCII);
    }

    internal static void WriteDChars(Span<byte> buffer, string str)
    {
        // Validate string
        if (!IsValidDString(str))
        {
            throw new IOException("Attempt to write string with invalid d-characters");
        }

        ////WriteASCII(buffer, offset, numBytes, true, str);
        WriteString(buffer, true, str, Encoding.ASCII);
    }

    internal static void WriteA1Chars(Span<byte> buffer, string str, Encoding enc)
    {
        // Validate string
        if (!IsValidAString(str))
        {
            throw new IOException("Attempt to write string with invalid a-characters");
        }

        WriteString(buffer, true, str, enc);
    }

    internal static void WriteD1Chars(Span<byte> buffer, string str, Encoding enc)
    {
        // Validate string
        if (!IsValidDString(str))
        {
            throw new IOException("Attempt to write string with invalid d-characters");
        }

        WriteString(buffer, true, str, enc);
    }

    internal static string ReadChars(ReadOnlySpan<byte> buffer, Encoding enc)
    {
        char[] chars;

        // Special handling for 'magic' names '\x00' and '\x01', which indicate root and parent, respectively
        if (buffer.Length == 1)
        {
            return new((char)buffer[0], 1);
        }

        var decoder = enc.GetDecoder();
        chars = new char[decoder.GetCharCount(buffer, false)];
        decoder.GetChars(buffer, chars, false);

        return new ReadOnlySpan<char>(chars).TrimEnd(' ').ToString();
    }

#if false
    public static byte WriteFileName(byte[] buffer, int offset, int numBytes, string str, Encoding enc)
    {
        if (numBytes > 255 || numBytes < 0)
        {
            throw new ArgumentOutOfRangeException("numBytes", "Attempt to write overlength or underlength file name");
        }

        // Validate string
        if (!isValidFileName(str))
        {
            throw new IOException("Attempt to write string with invalid file name characters");
        }

        return (byte)WriteString(buffer, offset, numBytes, false, str, enc);
    }

    public static byte WriteDirectoryName(byte[] buffer, int offset, int numBytes, string str, Encoding enc)
    {
        if (numBytes > 255 || numBytes < 0)
        {
            throw new ArgumentOutOfRangeException("numBytes", "Attempt to write overlength or underlength directory name");
        }

        // Validate string
        if (!isValidDirectoryName(str))
        {
            throw new IOException("Attempt to write string with invalid directory name characters");
        }

        return (byte)WriteString(buffer, offset, numBytes, false, str, enc);
    }
#endif

    internal static int WriteString(Span<byte> buffer, bool pad, string str, Encoding enc)
    {
        return WriteString(buffer, pad, str, enc, false);
    }

    internal static int WriteString(Span<byte> buffer, bool pad, string str, Encoding enc,
                                    bool canTruncate)
    {
        var encoder = enc.GetEncoder();

        var paddedString = pad ? str + new string(' ', buffer.Length) : str;

        // Assumption: never less than one byte per character

        encoder.Convert(paddedString.AsSpan(), buffer, false,
            out var charsUsed, out var bytesUsed, out _);

        if (!canTruncate && charsUsed < str.Length)
        {
            throw new IOException("Failed to write entire string");
        }

        return bytesUsed;
    }

    internal static bool IsValidAString(string str)
    {
        for (var i = 0; i < str.Length; ++i)
        {
            if (str[i] is not
                (>= ' ' and <= '\"'
                or >= '%' and <= '/'
                or >= ':' and <= '?'
                or >= '0' and <= '9'
                or >= 'A' and <= 'Z'
                or '_'))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsValidDString(string str)
    {
        for (var i = 0; i < str.Length; ++i)
        {
            if (!IsValidDChar(str[i]))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsValidDChar(char ch)
    {
        return ch is >= '0' and <= '9' or >= 'A' and <= 'Z' or '_';
    }

    internal static bool IsValidFileName(string str)
    {
        for (var i = 0; i < str.Length; ++i)
        {
            if (
str[i] is not (>= '0' and <= '9' or >= 'A' and <= 'Z' or '_' or
                  '.' or ';'))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsValidDirectoryName(string str)
    {
        if (str.Length == 1 && (str[0] == 0 || str[0] == 1))
        {
            return true;
        }

        return IsValidDString(str);
    }

    internal static string NormalizeFileName(string name)
    {
        var parts = SplitFileName(name);
        return parts[0] + '.' + parts[1] + ';' + parts[2];
    }

    internal static string[] SplitFileName(string name)
    {
        string[] parts = { name, string.Empty, "1" };

        if (name.Contains('.'))
        {
            var endOfFilePart = name.IndexOf('.');
            parts[0] = name.Substring(0, endOfFilePart);
            if (name.Contains(';'))
            {
                var verSep = name.IndexOf(';', endOfFilePart + 1);
                parts[1] = name.Substring(endOfFilePart + 1, verSep - (endOfFilePart + 1));
                parts[2] = name.Substring(verSep + 1);
            }
            else
            {
                parts[1] = name.Substring(endOfFilePart + 1);
            }
        }
        else
        {
            if (name.Contains(';'))
            {
                var verSep = name.IndexOf(';');
                parts[0] = name.Substring(0, verSep);
                parts[2] = name.Substring(verSep + 1);
            }
        }

        if (!ushort.TryParse(parts[2], out var ver) || ver > 32767 || ver < 1)
        {
            ver = 1;
        }

        parts[2] = ver.ToString(NumberFormatInfo.InvariantInfo);

        return parts;
    }

    /// <summary>
    /// Converts a DirectoryRecord time to UTC.
    /// </summary>
    /// <param name="data">Buffer containing the time data.</param>
    /// <returns>The time in UTC.</returns>
    internal static DateTime ToUTCDateTimeFromDirectoryTime(ReadOnlySpan<byte> data)
    {
        try
        {
            var relTime = new DateTime(
                1900 + data[0],
                data[1],
                data[2],
                data[3],
                data[4],
                data[5],
                DateTimeKind.Utc);
            return relTime - TimeSpan.FromMinutes(15 * (sbyte)data[6]);
        }
        catch (ArgumentOutOfRangeException)
        {
            // In case the ISO has a bad date encoded, we'll just fall back to using a fixed date
            return DateTime.MinValue;
        }
    }

    internal static void ToDirectoryTimeFromUTC(Span<byte> data, DateTime dateTime)
    {
        if (dateTime == DateTime.MinValue || dateTime.Year < 1900)
        {
            data.Slice(0, 7).Clear();
        }
        else
        {
            if (dateTime.Year < 1900)
            {
                throw new IOException("Year is out of range");
            }

            data[0] = (byte)(dateTime.Year - 1900);
            data[1] = (byte)dateTime.Month;
            data[2] = (byte)dateTime.Day;
            data[3] = (byte)dateTime.Hour;
            data[4] = (byte)dateTime.Minute;
            data[5] = (byte)dateTime.Second;
            data[6] = 0;
        }
    }

    internal static DateTime ToDateTimeFromVolumeDescriptorTime(ReadOnlySpan<byte> data)
    {
        var allNull = true;
        for (var i = 0; i < 16; ++i)
        {
            if (data[i] is not ((byte)'0') and not 0)
            {
                allNull = false;
                break;
            }
        }

        if (allNull)
        {
            return DateTime.MinValue;
        }

        var strForm = EncodingUtilities
            .GetLatin1Encoding()
            .GetString(data.Slice(0, 16));

        // Work around bugs in burning software that may use zero bytes (rather than '0' characters)
        strForm = strForm.Replace('\0', '0');

        var year = SafeParseInt(1, 9999, strForm.AsSpan(0, 4));
        var month = SafeParseInt(1, 12, strForm.AsSpan(4, 2));
        var day = SafeParseInt(1, 31, strForm.AsSpan(6, 2));
        var hour = SafeParseInt(0, 23, strForm.AsSpan(8, 2));
        var min = SafeParseInt(0, 59, strForm.AsSpan(10, 2));
        var sec = SafeParseInt(0, 59, strForm.AsSpan(12, 2));
        var hundredths = SafeParseInt(0, 99, strForm.AsSpan(14, 2));

        try
        {
            var time = new DateTime(year, month, day, hour, min, sec, hundredths * 10, DateTimeKind.Utc);
            return time - TimeSpan.FromMinutes(15 * (sbyte)data[16]);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.MinValue;
        }
    }

    internal static void ToVolumeDescriptorTimeFromUTC(Span<byte> buffer, DateTime dateTime)
    {
        if (dateTime == DateTime.MinValue)
        {
            for (var i = 0; i < 16; ++i)
            {
                buffer[i] = (byte)'0';
            }

            buffer[16] = 0;
            return;
        }

        var strForm = dateTime.ToString("yyyyMMddHHmmssff", CultureInfo.InvariantCulture);

        EncodingUtilities
            .GetLatin1Encoding()
            .GetBytes(strForm, buffer.Slice(0, 16));
        
        buffer[16] = 0;
    }

    internal static void EncodingToBytes(Encoding enc, Span<byte> data)
    {
        data.Slice(0, 32).Clear();

        if (enc == Encoding.ASCII)
        {
            // Nothing to do
        }
        else if (enc == Encoding.BigEndianUnicode)
        {
            data[0] = 0x25;
            data[1] = 0x2F;
            data[2] = 0x45;
        }
        else
        {
            throw new ArgumentException("Unrecognized character encoding");
        }
    }

    internal static Encoding EncodingFromBytes(ReadOnlySpan<byte> data)
    {
        var enc = Encoding.ASCII;
        if (data[0] == 0x25 && data[1] == 0x2F
            && (data[2] == 0x40 || data[2] == 0x43 || data[2] == 0x45))
        {
            // I.e. this is a joliet disc!
            enc = Encoding.BigEndianUnicode;
        }

        return enc;
    }

    internal static bool IsSpecialDirectory(DirectoryRecord r)
    {
        return r.FileIdentifier is "\0" or "\x01";
    }

    private static int SafeParseInt(int minVal, int maxVal, ReadOnlySpan<char> str)
    {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
        if (!int.TryParse(str, out var val))
        {
            return minVal;
        }
#else
        if (!int.TryParse(str.ToString(), out var val))
        {
            return minVal;
        }
#endif

        if (val < minVal)
        {
            return minVal;
        }

        if (val > maxVal)
        {
            return maxVal;
        }

        return val;
    }
}