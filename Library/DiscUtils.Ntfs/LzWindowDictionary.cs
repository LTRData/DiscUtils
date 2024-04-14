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

//
// Contributed by bsobel:
//   - Derived from Puyo tools (BSD license)
//

using System;
using System.Collections.Generic;

namespace DiscUtils.Ntfs;

internal sealed class LzWindowDictionary
{
    /// <summary>
    /// Index of locations of each possible byte value within the compression window.
    /// </summary>
    private readonly List<int>[] _offsetList;

    public LzWindowDictionary()
    {
        Initalize();

        // Build the index list, so Lz compression will become significantly faster 
        _offsetList = new List<int>[0x100];
        for (var i = 0; i < _offsetList.Length; i++)
        {
            _offsetList[i] = [];
        }
    }

    private int BlockSize { get; set; }

    public int MaxMatchAmount { get; set; }

    public int MinMatchAmount { get; set; }

    public void Reset()
    {
        Initalize();

        for (var i = 0; i < _offsetList.Length; i++)
        {
            _offsetList[i].Clear();
        }
    }

    public int[] Search(ReadOnlySpan<byte> decompressedData, int index, uint length)
    {
        RemoveOldEntries(decompressedData[index]); // Remove old entries for this index 

        int[] match = { 0, 0 };

        if (index < 1 || length - index < MinMatchAmount)
        {
            // Can't find matches if there isn't enough data 
            return match;
        }

        for (var i = 0; i < _offsetList[decompressedData[index]].Count; i++)
        {
            var matchStart = _offsetList[decompressedData[index]][i];
            var matchSize = 1;

            if (index - matchStart > BlockSize)
            {
                break;
            }

            var maxMatchSize =
                (int)Math.Min(Math.Min(MaxMatchAmount, BlockSize), Math.Min(length - index, length - matchStart));
            while (matchSize < maxMatchSize &&
                   decompressedData[index + matchSize] ==
                   decompressedData[matchStart + matchSize])
            {
                matchSize++;
            }

            if (matchSize >= MinMatchAmount && matchSize > match[1])
            {
                // This is a good match 
                match = new[] { (int)(index - matchStart), matchSize };

                if (matchSize == MaxMatchAmount)
                {
                    // Don't look for more matches 
                    break;
                }
            }
        }

        // Return the real match (or the default 0:0 match). 
        return match;
    }

    // Add entries 
    public void AddEntry(ReadOnlySpan<byte> decompressedData, int index)
    {
        _offsetList[decompressedData[index]].Add(index);
    }

    public void AddEntryRange(ReadOnlySpan<byte> decompressedData, int index, int length)
    {
        for (var i = 0; i < length; i++)
        {
            AddEntry(decompressedData, index + i);
        }
    }

    private void Initalize()
    {
        MinMatchAmount = 3;
        MaxMatchAmount = 18;
        BlockSize = 4096;
    }

    private void RemoveOldEntries(byte index)
    {
        while (_offsetList[index].Count > 256)
        {
            _offsetList[index].RemoveAt(0);
        }
    }
}