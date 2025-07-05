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
using System.Text;
using DiscUtils.Streams;
using LTRData.Extensions.Buffers;

namespace DiscUtils.Net.Dns;

internal sealed class PacketWriter
{
    private readonly byte[] _data;
    private int _pos;

    public PacketWriter(int maxSize)
    {
        _data = StreamUtilities.GetUninitializedArray<byte>(maxSize);
    }

    public void WriteName(string name)
    {
        // TODO: Implement compression
        var labels = name.Split('.', StringSplitOptions.RemoveEmptyEntries);

        foreach (var label in labels)
        {
            var labelBytes = Encoding.UTF8.GetBytes(label);
            if (labelBytes.Length > 63)
            {
                throw new ArgumentException($"Invalid DNS label - more than 63 octets '{label}' in '{name}'", nameof(name));
            }

            _data[_pos++] = (byte)labelBytes.Length;
            System.Buffer.BlockCopy(labelBytes, 0, _data, _pos, labelBytes.Length);
            _pos += labelBytes.Length;
        }

        _data[_pos++] = 0;
    }

    public void Write(ushort val)
    {
        EndianUtilities.WriteBytesBigEndian(val, _data, _pos);
        _pos += 2;
    }

    public byte[] GetBytes() => _data.AsSpan(0, _pos).ToArray();
}