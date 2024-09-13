//
// Copyright (c) 2024, Olof Lagerkvist and contributors
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
using System;
#if NET8_0_OR_GREATER
using System.Collections.Frozen;
using System.Collections.Immutable;
#elif NET462_OR_GREATER || NETSTANDARD || NETCOREAPP
using System.Collections.Immutable;
#endif
using DiscUtils.Streams;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Collections.Concurrent;

namespace DiscUtils.Fat;

/// <summary>
/// Fast encoding 1-byte to 1-char table to help encoding/decoding short file names.
/// </summary>
internal sealed class FastEncodingTable
{
#if NET8_0_OR_GREATER
    private readonly FrozenDictionary<char, byte> _mapUpperCharToByte;
    private readonly ImmutableArray<char> _mapByteToChar;

    private static readonly ConcurrentDictionary<Encoding, (FrozenDictionary<char, byte> mapUpperCharToByte, ImmutableArray<char> mapByteToChar)> _mappingCache = new();
#elif NET462_OR_GREATER || NETSTANDARD || NETCOREAPP
    private readonly ImmutableDictionary<char, byte> _mapUpperCharToByte;
    private readonly ImmutableArray<char> _mapByteToChar;

    private static readonly ConcurrentDictionary<Encoding, (ImmutableDictionary<char, byte> mapUpperCharToByte, ImmutableArray<char> mapByteToChar)> _mappingCache = new();
#else
    private readonly Dictionary<char, byte> _mapUpperCharToByte;
    private readonly char[] _mapByteToChar;

    private static readonly ConcurrentDictionary<Encoding, (Dictionary<char, byte> mapUpperCharToByte, char[] mapByteToChar)> _mappingCache = new();
#endif

#if !NETFRAMEWORK
    static FastEncodingTable()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
#endif

    /// <summary>
    /// Gets the default encoding table for the IBM PC code page 437.
    /// </summary>
    public static readonly FastEncodingTable Default = new(Encoding.GetEncoding(437));
    
    /// <summary>
    /// Used to replace characters that cannot be encoded.
    /// </summary>
    public const char ReplacementChar = '\ufffd';

    /// <summary>
    /// 
    /// </summary>
    /// <param name="encoding"></param>
    public FastEncodingTable(Encoding encoding)
    {
        Debug.Assert(encoding.IsSingleByte);
        Encoding = encoding;

        var (mapUpperCharToByte, mapByteToChar) = _mappingCache.GetOrAdd(encoding,
            static encoding =>
            {
                var mapByteToChar = new char[256];
                var mapUpperCharToByte = new Dictionary<char, byte>(256);

                Span<char> c = stackalloc char[1];

                // Calculate the mapping from byte to char
                for (int i = 0; i < 256; ++i)
                {
                    var b = (byte)i;
                    c[0] = default;

                    int encoded = encoding.GetChars([b], c);

                    if (encoded != 1)
                    {
                        c[0] = ReplacementChar;
                    }

                    mapByteToChar[i] = c[0];
                    mapUpperCharToByte[c[0]] = b;
                }

                // Calculate the mapping from upper char to byte
                for (int i = 0; i < 256; ++i)
                {
                    var chr = mapByteToChar[i];
                    var upperChar = char.ToUpperInvariant(chr);

                    if (mapUpperCharToByte.TryGetValue(upperChar, out var bUpper))
                    {
                        mapUpperCharToByte[chr] = bUpper;
                    }
                }

#if NET8_0_OR_GREATER
                var frozenMapUpperCharToByte = mapUpperCharToByte.ToFrozenDictionary();
                var frozenMapByteToChar = mapByteToChar.ToImmutableArray();
#elif NET462_OR_GREATER || NETSTANDARD || NETCOREAPP
                var frozenMapUpperCharToByte = mapUpperCharToByte.ToImmutableDictionary();
                var frozenMapByteToChar = mapByteToChar.ToImmutableArray();
#else
                var frozenMapUpperCharToByte = mapUpperCharToByte;
                var frozenMapByteToChar = mapByteToChar;
#endif

                return (frozenMapUpperCharToByte, frozenMapByteToChar);
            });

        _mapUpperCharToByte = mapUpperCharToByte;
        _mapByteToChar = mapByteToChar;
    }

    /// <summary>
    /// The encoding used to create the table.
    /// </summary>
    public Encoding Encoding { get; }

    /// <summary>
    /// Gets the char for the specified byte.
    /// </summary>
    /// <param name="b">The byte to convert.</param>
    /// <returns>The char converted or <see cref="ReplacementChar"/> if no mapping exists.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public char GetCharFromByte(byte b) => _mapByteToChar[(int)b];

    /// <summary>
    /// Gets the upper case byte for the specified char.
    /// </summary>
    /// <param name="c">The char to convert.</param>
    /// <param name="b">The upper case char converted if this method returns <c>true</c></param>
    /// <returns><c>true</c> if the char was converted; otherwise, <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetCharToByteUpperCase(char c, out byte b) => _mapUpperCharToByte.TryGetValue(c, out b);
}