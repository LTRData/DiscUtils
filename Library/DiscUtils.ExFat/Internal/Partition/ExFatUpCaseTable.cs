// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DiscUtils.ExFat.Internal.Partition;
/// <summary>
/// Up-case table
/// </summary>
public class ExFatUpCaseTable
{
    private readonly IDictionary<char, char> _table = new Dictionary<char, char>();

    /// <summary>
    /// Sets the default table.
    /// </summary>
    public void SetDefault()
    {
        _table.Clear();
        //for (var c = 'a'; c <= 'z'; c++)
        for (var c = (char)0; c < (char)0xFFFF; c++)
        {
            var uc = char.ToUpper(c);
            if (uc != c)
            {
                _table[c] = uc;
            }
        }
    }

    /// <summary>
    /// Reads the specified upcase table stream.
    /// </summary>
    /// <param name="upcaseTableStream">The upcase table stream.</param>
    public void Read(Stream upcaseTableStream)
    {
        _table.Clear();
        Span<byte> pairBytes = stackalloc byte[2];
        var currentChar = '\0';
        var settingCurrentChar = false;
        for (; ; )
        {
            if (upcaseTableStream.Read(pairBytes) != 2)
            {
                break;
            }

            var c = (char)EndianUtilities.ToUInt16LittleEndian(pairBytes);
            // short form: FFFF <char> sets the next char to be set
            // otherwise this is indexed
            if (c == 0xFFFF)
            {
                settingCurrentChar = true;
            }
            else if (settingCurrentChar)
            {
                currentChar += c;
                settingCurrentChar = false;
            }
            else
            {
                if (currentChar != c)
                {
                    _table[currentChar] = c;
                }

                ++currentChar;
            }
        }
    }

    /// <summary>
    /// Writes the table to specified stream.
    /// </summary>
    /// <param name="stream">The stream.</param>
    public uint Write(Stream stream)
    {
        uint checksum = 0;
        var current = 0;
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        EndianUtilities.WriteBytesLittleEndian((ushort)0xFFFF, buffer);
        foreach (var lc in _table.Keys.OrderBy(c => c))
        {
            // something to skip
            if (lc != current)
            {
                Write(stream, buffer, ref checksum);
                EndianUtilities.WriteBytesLittleEndian((ushort)(lc - current), buffer);
                Write(stream, buffer, ref checksum);
            }

            EndianUtilities.WriteBytesLittleEndian(_table[lc], buffer);
            Write(stream, buffer, ref checksum);
            current = lc + 1;
        }

        return checksum;
    }

    private static void Write(Stream stream, ReadOnlySpan<byte> bs, ref uint c)
    {
        foreach (var b in bs)
        {
            Write(stream, b, ref c);
        }
    }

    private static void Write(Stream stream, byte b, ref uint c)
    {
        stream.WriteByte(b);
        c = c.RotateRight() + b;
    }

    /// <summary>
    /// To the upper.
    /// </summary>
    /// <param name="c">The c.</param>
    /// <returns></returns>
    public char ToUpper(char c)
    {
        if (_table.TryGetValue(c, out var uc))
        {
            return uc;
        }

        return c;
    }
}