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
using System.Runtime.InteropServices;
using System.Text;
using DiscUtils.Internal;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Fat;

internal sealed class FileName : IEquatable<FileName>
{
    private const byte SpaceByte = 0x20;

    public static readonly FileName SelfEntryName =
        new(".          "u8);

    public static readonly FileName ParentEntryName =
        new("..         "u8);

    public static readonly FileName Null =
        new("\0\0\0\0\0\0\0\0\0\0\0"u8);

    private static readonly byte[] InvalidBytes = "\"*+,./:;<=>?[\\]|"u8.ToArray();

    private readonly byte[] _raw;

    private readonly string _lfn;

    public FileName(ReadOnlySpan<byte> data)
    {
        var offset = 0;

        // LFN
        // ToDo: This code is not endian-safe. Long file names will be messed
        // up when this code runs on big endian machines.
        if ((data[0] & 0xc0) == 0x40 && data[11] == 0x0f)
        {
            var lfn_entries = data[0] & 0x3f;

            Span<char> lfn_chars = stackalloc char[13 * lfn_entries];
            
            lfn_chars.Clear();

            var lfn_bytes = MemoryMarshal.AsBytes(lfn_chars);

            for (var i = lfn_entries;
                i > 0 && (data[offset] & 0x3f) == i && data[offset + 11] == 0x0f;
                i--, offset += 32)
            {
                data.Slice(offset + 1, 10).CopyTo(lfn_bytes.Slice(26 * (i - 1)));
                data.Slice(offset + 14, 12).CopyTo(lfn_bytes.Slice(26 * (i - 1) + 10));
                data.Slice(offset + 28, 4).CopyTo(lfn_bytes.Slice(26 * (i - 1) + 22));
            }

            var nullpos = lfn_chars.IndexOf('\0');

            if (nullpos < 0)
            {
                nullpos = lfn_chars.Length;
            }

            _lfn = lfn_chars.Slice(0, nullpos).ToString();
        }

        _raw = new byte[11];

        data.Slice(offset, 11).CopyTo(_raw);
    }

    public FileName(string name, Encoding encoding)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(name);
#else
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }
#endif

        if (name.Length > 255)
        {
            throw new IOException($"Too long name: '{name}'");
        }

        _raw = new byte[11];

        Span<byte> bytes = stackalloc byte[encoding.GetByteCount(name)];
        
        encoding.GetBytes(name, bytes);

        if (bytes.Length == 0)
        {
            throw new ArgumentException($"File name too short '{name}'", nameof(name));
        }

        var nameIdx = 0;
        var rawIdx = 0;

        var extensionPosition = bytes.LastIndexOf((byte)'.');

        // If not dot, or last dot is at the beginning, the name
        // is considered to have no extension
        if (extensionPosition <= 0)
        {
            extensionPosition = bytes.Length;
        }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
        Span<char> buffer = stackalloc char[4];
#endif

        for (; nameIdx < extensionPosition && rawIdx < 8;nameIdx++)
        {
            var b = bytes[nameIdx];

            if (b == '.')
            {
                continue;
            }

            if (b == '?')
            {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
                ((int)name[nameIdx]).TryFormat(buffer, out var hexLength, format: "X");
#else
                var buffer = ((int)name[nameIdx]).ToString("X");
                var hexLength = buffer.Length;
#endif

                for (var i = 0; i < hexLength && rawIdx < 8; i++)
                {
                    _raw[rawIdx++] = (byte)buffer[i];
                }

                continue;
            }

            if (b < 0x20 || Contains(InvalidBytes, b))
            {
                throw new ArgumentException($"Invalid character in file name '{(char)b}'", nameof(name));
            }

            _raw[rawIdx++] = (byte)char.ToUpperInvariant((char)b);
        }

        if (rawIdx > 8)
        {
            //throw new ArgumentException($"File name too long '{name}'", nameof(name));
        }

        if (rawIdx == 0)
        {
            //throw new ArgumentException($"File name too short '{name}'", nameof(name));
        }

        while (rawIdx < 8)
        {
            _raw[rawIdx++] = SpaceByte;
        }

        if (nameIdx < bytes.Length && bytes[nameIdx] == '.')
        {
            ++nameIdx;
        }

        for (; nameIdx < bytes.Length && rawIdx < _raw.Length; nameIdx++)
        {
            var b = bytes[nameIdx];

            if (b == '?')
            {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
                ((int)name[nameIdx]).TryFormat(buffer, out var hexLength, format: "X");
#else
                var buffer = ((int)name[nameIdx]).ToString("X");
                var hexLength = buffer.Length;
#endif

                for (var i = 0; i < hexLength && rawIdx < _raw.Length; i++)
                {
                    _raw[rawIdx++] = (byte)buffer[i];
                }

                continue;
            }

            if (b < 0x20 || Contains(InvalidBytes, b))
            {
                throw new ArgumentException($"Invalid character in file extension '{(char)b}'", nameof(name));
            }

            _raw[rawIdx++] = b;
        }

        while (rawIdx < 11)
        {
            _raw[rawIdx++] = SpaceByte;
        }

        if (nameIdx != bytes.Length)
        {
            //throw new ArgumentException($"File extension too long '{name}'", nameof(name));
        }

        _lfn = name;
    }

    public bool Equals(FileName other) => Equals(this, other);

    public static bool Equals(FileName a, FileName b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        if (a._lfn != null)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(a._lfn, b._lfn) ||
                StringComparer.OrdinalIgnoreCase.Equals(a._lfn, b.GetShortName(Encoding.ASCII)))
            {
                return true;
            }
        }
        else if (b._lfn != null)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(a.GetShortName(Encoding.ASCII), b._lfn))
            {
                return true;
            }
        }
        else
        {
            if (CompareRawNames(a, b) == 0)
            {
                return true;
            }
        }

        return false;
    }

    public static FileName FromPath(string path, Encoding encoding)
    {
        return new FileName(Utilities.GetFileFromPath(path), encoding);
    }

    public static bool operator ==(FileName a, FileName b) => Equals(a, b);

    public static bool operator !=(FileName a, FileName b) => !Equals(a, b);

    public string GetDisplayName(Encoding encoding)
    {
        return _lfn ?? GetShortName(encoding);
    }

    public string GetShortName(Encoding encoding)
    {
        return $"{encoding.GetString(_raw, 0, 8).TrimEnd()}.{encoding.GetString(_raw, 8, 3)}".TrimEnd('.', ' ');
    }

    public void SetShortName(string name, Encoding encoding)
    {
        var bytes = encoding.GetBytes(name.ToUpperInvariant());

        var nameIdx = 0;
        var rawIdx = 0;

        while (nameIdx < bytes.Length && bytes[nameIdx] != '.' && rawIdx < _raw.Length)
        {
            var b = bytes[nameIdx++];
            if (b < 0x20 || Contains(InvalidBytes, b))
            {
                throw new ArgumentException($"Invalid character in file name '{(char)b}'", nameof(name));
            }

            _raw[rawIdx++] = b;
        }

        if (rawIdx > 8)
        {
            throw new ArgumentException($"File name too long '{name}'", nameof(name));
        }

        if (rawIdx == 0)
        {
            throw new ArgumentException($"File name too short '{name}'", nameof(name));
        }

        while (rawIdx < 8)
        {
            _raw[rawIdx++] = SpaceByte;
        }

        if (nameIdx < bytes.Length && bytes[nameIdx] == '.')
        {
            ++nameIdx;
        }

        while (nameIdx < bytes.Length && rawIdx < _raw.Length)
        {
            var b = bytes[nameIdx++];
            if (b < 0x20 || Contains(InvalidBytes, b))
            {
                throw new ArgumentException($"Invalid character in file extension '{(char)b}'", nameof(name));
            }

            _raw[rawIdx++] = b;
        }

        while (rawIdx < 11)
        {
            _raw[rawIdx++] = SpaceByte;
        }

        if (nameIdx != bytes.Length)
        {
            throw new ArgumentException($"File extension too long '{name}'", nameof(name));
        }
    }

    public bool IsMatch(Func<string, bool> filter, Encoding encoding)
    {
        var search_name = GetDisplayName(encoding);
        if (search_name.IndexOf('.') < 0)
        {
            search_name += '.';
        }

        return filter(search_name);
    }

    public string GetRawName(Encoding encoding) => encoding.GetString(_raw, 0, 11).TrimEnd();

    public FileName Deleted()
    {
        Span<byte> data = stackalloc byte[11];
        _raw.AsSpan(0, 11).CopyTo(data);
        data[0] = 0xE5;

        return new FileName(data);
    }

    public bool IsDeleted()
    {
        return _raw[0] == 0xE5;
    }

    public bool IsEndMarker()
    {
        return _raw[0] == 0x00;
    }

    public void GetBytes(Span<byte> data)
    {
        _raw.AsSpan(0, 11).CopyTo(data);
    }

    public override bool Equals(object other)
        => other is FileName otherName && Equals(this, otherName);

    public override int GetHashCode()
    {
        if (_lfn != null)
        {
            return _lfn.GetHashCode();
        }

        var val = new HashCode();

        for (var i = 0; i < 11; ++i)
        {
            val.Add(_raw[i]);
        }

        return val.ToHashCode();
    }

    private static int CompareRawNames(FileName a, FileName b)
    {
        if (a._lfn != null || b._lfn != null)
        {
            return StringComparer.OrdinalIgnoreCase.Compare(a.ToString(), b.ToString());
        }

        for (var i = 0; i < 11; ++i)
        {
            if (a._raw[i] != b._raw[i])
            {
                return a._raw[i] - b._raw[i];
            }
        }

        return 0;
    }

    private static bool Contains(byte[] array, byte val) => Array.IndexOf(array, val) >= 0;

    public string Lfn => _lfn;

    public string ShortName => GetShortName(Encoding.ASCII);

    public override string ToString() => GetDisplayName(Encoding.ASCII);
}