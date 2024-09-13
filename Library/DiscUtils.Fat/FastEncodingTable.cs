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
#if NET8_0_OR_GREATER
using System;
using System.Collections.Frozen;
#endif
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace DiscUtils.Fat;

/// <summary>
/// Fast encoding 1-byte to 1-char table to help encoding/decoding short file names.
/// </summary>
internal sealed unsafe class FastEncodingTable
{
    private readonly char[] _mapByteToChar;
#if NET8_0_OR_GREATER
    private readonly FrozenDictionary<char, byte> _mapUpperCharToByte;
#else
    private readonly Dictionary<char, byte> _mapUpperCharToByte;
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

        _mapByteToChar = new char[256];
        var mapUpperCharToByte = new Dictionary<char, byte>(256);

        // Calculate the mapping from byte to char
        for (int i = 0; i < 256; ++i)
        {
            byte b = (byte)i;
            char c = (char)0;
            int encoded = encoding.GetChars(&b, 1, &c, 1);
            if (encoded != 1)
            {
                c = ReplacementChar;
            }
            _mapByteToChar[i] = c;
            mapUpperCharToByte[c] = b;
        }

        // Calculate the mapping from upper char to byte
        for (int i = 0; i < 256; ++i)
        {
            var c = _mapByteToChar[i];
            var upperChar = char.ToUpperInvariant(c);
            if (mapUpperCharToByte.TryGetValue(upperChar, out var bUpper))
            {
                mapUpperCharToByte[c] = bUpper;
            }
        }
#if NET8_0_OR_GREATER
        _mapUpperCharToByte = mapUpperCharToByte.ToFrozenDictionary();
#else
        _mapUpperCharToByte = mapUpperCharToByte;
#endif
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