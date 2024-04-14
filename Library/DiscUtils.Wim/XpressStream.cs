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
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Compression;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Wim;

/// <summary>
/// Implements the XPRESS decompression algorithm.
/// </summary>
/// <remarks>This class is optimized for the case where the entire stream contents
/// fit into memory, it is not suitable for unbounded streams.</remarks>
internal class XpressStream : ReadOnlyCompatibilityStream
{
    private readonly byte[] _buffer;
    private readonly Stream _compressedStream;
    private long _position;

    /// <summary>
    /// Initializes a new instance of the XpressStream class.
    /// </summary>
    /// <param name="compressed">The stream of compressed data.</param>
    /// <param name="count">The length of this stream (in uncompressed bytes).</param>
    public XpressStream(Stream compressed, int count)
    {
        _compressedStream = new BufferedStream(compressed);
        _buffer = Buffer(count);
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override long Length => _buffer.Length;

    public override long Position
    {
        get => _position;

        set => _position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position > Length)
        {
            return 0;
        }

        var numToRead = (int)Math.Min(count, _buffer.Length - _position);
        System.Buffer.BlockCopy(_buffer, (int)_position, buffer, offset, numToRead);
        _position += numToRead;
        return numToRead;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_position > Length)
        {
            return Task.FromResult(0);
        }

        var numToRead = (int)Math.Min(count, _buffer.Length - _position);
        System.Buffer.BlockCopy(_buffer, (int)_position, buffer, offset, numToRead);
        _position += numToRead;
        return Task.FromResult(numToRead);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (_position > Length)
        {
            return new(0);
        }

        var numToRead = (int)Math.Min(buffer.Length, _buffer.Length - _position);
        _buffer.AsMemory((int)_position, numToRead).CopyTo(buffer);
        _position += numToRead;
        return new(numToRead);
    }

    public override int Read(Span<byte> buffer)
    {
        if (_position > Length)
        {
            return 0;
        }

        var numToRead = (int)Math.Min(buffer.Length, _buffer.Length - _position);
        _buffer.AsSpan((int)_position, numToRead).CopyTo(buffer);
        _position += numToRead;
        return numToRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    private HuffmanTree ReadHuffmanTree()
    {
        var lengths = new uint[256 + 16 * 16];

        for (var i = 0; i < lengths.Length; i += 2)
        {
            var b = ReadCompressedByte();

            lengths[i] = (uint)(b & 0xF);
            lengths[i + 1] = (uint)(b >> 4);
        }

        return new HuffmanTree(lengths);
    }

    private byte[] Buffer(int count)
    {
        var buffer = new byte[count];
        var numRead = 0;

        var tree = ReadHuffmanTree();
        var bitStream = new XpressBitStream(_compressedStream);

        while (numRead < count)
        {
            var symbol = tree.NextSymbol(bitStream);
            if (symbol < 256)
            {
                // The first 256 symbols are literal byte values
                buffer[numRead] = (byte)symbol;
                numRead++;
            }
            else
            {
                // The next 256 symbols are 4 bits each for offset and length.
                var offsetBits = (int)((symbol - 256) / 16);
                var len = (int)((symbol - 256) % 16);

                // The actual offset
                var offset = (int)((1 << offsetBits) - 1 + bitStream.Read(offsetBits));

                // Lengths up to 15 bytes are stored directly in the symbol bits, beyond that
                // the length is stored in the compression stream.
                if (len == 15)
                {
                    // Note this access is directly to the underlying stream - we're not going
                    // through the bit stream.  This makes the precise behaviour of the bit stream,
                    // in terms of read-ahead critical.
                    var b = ReadCompressedByte();

                    if (b == 0xFF)
                    {
                        // Again, note this access is directly to the underlying stream - we're not going
                        // through the bit stream.
                        len = ReadCompressedUShort();
                    }
                    else
                    {
                        len += b;
                    }
                }

                // Minimum length for a match is 3 bytes, so all lengths are stored as an offset
                // from 3.
                len += 3;

                // Simply do the copy
                for (var i = 0; i < len; ++i)
                {
                    buffer[numRead] = buffer[numRead - offset - 1];
                    numRead++;
                }
            }
        }

        return buffer;
    }

    private int ReadCompressedByte()
    {
        var b = _compressedStream.ReadByte();
        if (b < 0)
        {
            throw new InvalidDataException("Truncated stream");
        }

        return b;
    }

    private int ReadCompressedUShort()
    {
        var result = ReadCompressedByte();
        return result | ReadCompressedByte() << 8;
    }
}