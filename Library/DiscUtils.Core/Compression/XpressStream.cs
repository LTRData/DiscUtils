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
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Compression;

/// <summary>
/// Implements the XPRESS Huffman decompression algorithm.
/// </summary>
/// <remarks>The whole compressed stream is read to its end and decoded into memory when the instance is created
/// (with <see cref="XpressHuffman.TryDecompress(System.ReadOnlySpan{byte}, System.Span{byte}, out int, out int)"/>), so it is not suitable for unbounded streams.</remarks>
public class XpressStream : ReadOnlyCompatibilityStream
{
    private readonly byte[] _buffer;
    private long _position;

    /// <summary>
    /// Initializes a new instance of the XpressStream class.
    /// </summary>
    /// <param name="compressed">The stream of compressed data.</param>
    /// <param name="count">The length of this stream (in uncompressed bytes).</param>
    /// <exception cref="InvalidDataException">The compressed data is truncated or invalid.</exception>
    public XpressStream(Stream compressed, int count)
    {
        _buffer = Decompress(compressed, count);
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

    /// <summary>Reads the compressed stream to its end and decodes it with <see cref="XpressHuffman.TryDecompress(System.ReadOnlySpan{byte}, System.Span{byte}, out int, out int)"/>.</summary>
    private static byte[] Decompress(Stream compressed, int count)
    {
        byte[] input;
        using (var all = new MemoryStream())
        {
            compressed.CopyTo(all);
            input = all.ToArray();
        }

        var buffer = new byte[count];
        if (!XpressHuffman.TryDecompress(input, buffer, out _, out var written) || written != count)
        {
            throw new InvalidDataException("Truncated or invalid XPRESS Huffman stream");
        }

        return buffer;
    }
}