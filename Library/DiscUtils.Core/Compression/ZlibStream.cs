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
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;
using LTRData.Extensions.Async;

namespace DiscUtils.Compression;

/// <summary>
/// Implementation of the Zlib compression algorithm.
/// </summary>
/// <remarks>Only decompression is currently implemented.</remarks>
public class ZlibStream : CompatibilityStream
{
    private readonly Adler32 _adler32;
    private readonly DeflateStream _deflateStream;
    private readonly CompressionMode _mode;
    private readonly Stream _stream;

    /// <summary>
    /// Initializes a new instance of the ZlibStream class.
    /// </summary>
    /// <param name="stream">The stream to compress of decompress.</param>
    /// <param name="mode">Whether to compress or decompress.</param>
    /// <param name="leaveOpen">Whether closing this stream should leave <c>stream</c> open.</param>
    public ZlibStream(Stream stream, CompressionMode mode, bool leaveOpen)
    {
        _stream = stream;
        _mode = mode;

        if (mode == CompressionMode.Decompress)
        {
            // We just sanity check against expected header values...
            Span<byte> headerBuffer = stackalloc byte[sizeof(ushort)];
            stream.ReadExactly(headerBuffer);
            var header = EndianUtilities.ToUInt16BigEndian(headerBuffer);

            if (header % 31 != 0)
            {
                throw new IOException("Invalid Zlib header found");
            }

            if ((header & 0x0F00) != 8 << 8)
            {
                throw new NotSupportedException("Zlib compression not using DEFLATE algorithm");
            }

            if ((header & 0x0020) != 0)
            {
                throw new NotSupportedException("Zlib compression using preset dictionary");
            }
        }
        else
        {
            ushort header =
                (8 << 8) // DEFLATE
                | (7 << 12) // 32K window size
                | 0x80; // Default algorithm
            header |= (ushort)(31 - header % 31);

            Span<byte> headerBuffer = stackalloc byte[2];
            EndianUtilities.WriteBytesBigEndian(header, headerBuffer);
            stream.Write(headerBuffer);
        }

        _deflateStream = new DeflateStream(stream, mode, leaveOpen);
        _adler32 = new Adler32();
    }

    /// <summary>
    /// Gets whether the stream can be read.
    /// </summary>
    public override bool CanRead => _deflateStream.CanRead;

    /// <summary>
    /// Gets whether the stream pointer can be changed.
    /// </summary>
    public override bool CanSeek => false;

    /// <summary>
    /// Gets whether the stream can be written to.
    /// </summary>
    public override bool CanWrite => _deflateStream.CanWrite;

    /// <summary>
    /// Gets the length of the stream.
    /// </summary>
    public override long Length => throw new NotSupportedException();

    /// <summary>
    /// Gets and sets the stream position.
    /// </summary>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>
    /// Closes the stream.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (_mode == CompressionMode.Decompress)
        {
            // Can only check Adler checksum on seekable streams.  Since DeflateStream
            // aggresively caches input, it normally has already consumed the footer.
            if (_stream.CanSeek)
            {
                _stream.Seek(-4, SeekOrigin.End);
                Span<byte> footerBuffer = stackalloc byte[sizeof(int)];
                _stream.ReadExactly(footerBuffer);
                if (EndianUtilities.ToInt32BigEndian(footerBuffer) != _adler32.Value)
                {
                    throw new InvalidDataException("Corrupt decompressed data detected");
                }
            }

            _deflateStream.Dispose();
        }
        else
        {
            _deflateStream.Dispose();

            Span<byte> footerBuffer = stackalloc byte[4];
            EndianUtilities.WriteBytesBigEndian(_adler32.Value, footerBuffer);
            _stream.Write(footerBuffer);
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Flushes the stream.
    /// </summary>
    public override void Flush()
    {
        _deflateStream.Flush();
    }

    /// <summary>
    /// Reads data from the stream.
    /// </summary>
    /// <param name="buffer">The buffer to populate.</param>
    /// <param name="offset">The first byte to write.</param>
    /// <param name="count">The number of bytes requested.</param>
    /// <returns>The number of bytes read.</returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        CheckParams(buffer, offset, count);

        var numRead = _deflateStream.Read(buffer, offset, count);
        _adler32.Process(buffer.AsSpan(offset, numRead));
        return numRead;
    }

    /// <summary>
    /// Reads data from the stream.
    /// </summary>
    /// <param name="buffer">The buffer to populate.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The number of bytes read.</returns>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var numRead = await _deflateStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        _adler32.Process(buffer.Span.Slice(0, numRead));
        return numRead;
    }

    /// <summary>
    /// Reads data from the stream.
    /// </summary>
    /// <param name="buffer">The buffer to populate.</param>
    /// <returns>The number of bytes read.</returns>
    public override int Read(Span<byte> buffer)
    {
        var numRead = _deflateStream.Read(buffer);
        _adler32.Process(buffer.Slice(0, numRead));
        return numRead;
    }

    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state) =>
        ReadAsync(buffer, offset, count, CancellationToken.None).AsAsyncResult(callback, state);

    public override int EndRead(IAsyncResult asyncResult) => ((Task<int>)asyncResult).GetAwaiter().GetResult();

    /// <summary>
    /// Seeks to a new position.
    /// </summary>
    /// <param name="offset">Relative position to seek to.</param>
    /// <param name="origin">The origin of the seek.</param>
    /// <returns>The new position.</returns>
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Changes the length of the stream.
    /// </summary>
    /// <param name="value">The new desired length of the stream.</param>
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Writes data to the stream.
    /// </summary>
    /// <param name="buffer">Buffer containing the data to write.</param>
    /// <param name="offset">Offset of the first byte to write.</param>
    /// <param name="count">Number of bytes to write.</param>
    public override void Write(byte[] buffer, int offset, int count)
    {
        CheckParams(buffer, offset, count);

        _adler32.Process(buffer.AsSpan(offset, count));
        _deflateStream.Write(buffer, offset, count);
    }

    /// <summary>
    /// Writes data to the stream.
    /// </summary>
    /// <param name="buffer">Buffer containing the data to write.</param>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _adler32.Process(buffer);
        _deflateStream.Write(buffer);
    }

    /// <summary>
    /// Writes data to the stream.
    /// </summary>
    /// <param name="buffer">Buffer containing the data to write.</param>
    /// <param name="cancellationToken"></param>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        _adler32.Process(buffer.Span);
        return _deflateStream.WriteAsync(buffer, cancellationToken);
    }

    /// <summary>
    /// Writes data to the stream.
    /// </summary>
    /// <param name="buffer">Buffer containing the data to write.</param>
    /// <param name="offset">Offset of the first byte to write.</param>
    /// <param name="count">Number of bytes to write.</param>
    /// <param name="cancellationToken"></param>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        CheckParams(buffer, offset, count);

        _adler32.Process(buffer.AsSpan(offset, count));
        return _deflateStream.WriteAsync(buffer, offset, count, cancellationToken);
    }

    private static void CheckParams(byte[] buffer, int offset, int count)
    {
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if (offset < 0 || offset > buffer.Length)
        {
            throw new ArgumentException("Offset outside of array bounds", nameof(offset));
        }

        if (count < 0 || offset + count > buffer.Length)
        {
            throw new ArgumentException("Array index out of bounds", nameof(count));
        }
    }
}