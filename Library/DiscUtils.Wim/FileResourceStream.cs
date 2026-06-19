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

using DiscUtils.Compression;
using DiscUtils.Streams;
using LTRData.Extensions.Buffers;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DiscUtils.Wim;

/// <summary>
/// Provides access to a (compressed) resource within the WIM file.
/// </summary>
/// <remarks>Stream access must be strictly sequential.</remarks>
internal class FileResourceStream : SparseStream.ReadOnlySparseStream
{
    private readonly Stream _baseStream;
    private readonly long[] _chunkLength;

    private readonly long[] _chunkOffsets;
    private readonly int _chunkSize;

    private int _currentChunk;
    private byte[]? _chunkBuffer;
    private ReadOnlyMemory<byte> _currentChunkData;
    private readonly ShortResourceHeader _header;
    private readonly IBlockDecompressor? _blockDecompressor;
    private readonly long _offsetDelta;

    private long _position;

    public FileResourceStream(Stream baseStream, ShortResourceHeader header, FileFlags fileFlags, int chunkSize)
    {
        _baseStream = baseStream;
        _header = header;
        _blockDecompressor = GetDecompressor(fileFlags);
        _chunkSize = chunkSize;

        if (baseStream.Length > uint.MaxValue)
        {
            throw new NotImplementedException("Large files >4GB");
        }

        var numChunks = (int)MathUtilities.Ceil(header.OriginalSize, _chunkSize);

        _chunkOffsets = new long[numChunks];
        _chunkLength = new long[numChunks];
        Span<byte> buffer = stackalloc byte[4];
        for (var i = 1; i < numChunks; ++i)
        {
            _baseStream.ReadExactly(buffer);
            _chunkOffsets[i] = EndianUtilities.ToUInt32LittleEndian(buffer);
            _chunkLength[i - 1] = _chunkOffsets[i] - _chunkOffsets[i - 1];
        }

        _chunkLength[numChunks - 1] = _baseStream.Length - _baseStream.Position - _chunkOffsets[numChunks - 1];
        _offsetDelta = _baseStream.Position;

        _currentChunk = -1;
    }

    private static IBlockDecompressor? GetDecompressor(FileFlags flags)
    {
        if ((flags & FileFlags.LzxCompression) != 0)
        {
            return new Lzx(windowBits: 15);
        }
        else if ((flags & FileFlags.XpressCompression) != 0)
        {
            return XpressHuffman.Default;
        }

        return null;
    }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;

        if (disposing)
        {
            if (_blockDecompressor is IDisposable disposable)
            {
                disposable.Dispose();
            }

            if (_chunkBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(_chunkBuffer);
                _chunkBuffer = null;
            }
        }

        base.Dispose(disposing);
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override IEnumerable<StreamExtent> Extents
        => SingleValueEnumerable.Get(new StreamExtent(0, Length));

    public override long Length => _header.OriginalSize;

    public override long Position
    {
        get => _position;

        set => _position = value;
    }

    public bool IsDisposed { get; private set; }

    public override int Read(byte[] buffer, int offset, int count)
    {
#if NET7_0_OR_GREATER
        ObjectDisposedException.ThrowIf(IsDisposed, this);
#else
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(FileResourceStream));
        }
#endif

        if (_position >= Length)
        {
            return 0;
        }

        var maxToRead = (int)Math.Min(Length - _position, count);

        var totalRead = 0;

        while (totalRead < maxToRead)
        {
            var chunk = (int)(_position / _chunkSize);
            var chunkOffset = (int)(_position % _chunkSize);
            var numToRead = Math.Min(maxToRead - totalRead, _chunkSize - chunkOffset);

            if (numToRead == 0)
            {
                return totalRead;
            }

            if (_currentChunk != chunk || _currentChunkData.IsEmpty)
            {
                _chunkBuffer ??= ArrayPool<byte>.Shared.Rent(_chunkSize);
                _currentChunkData = new(_chunkBuffer, 0, DecompressChunk(chunk, _chunkBuffer));
                _currentChunk = chunk;
            }

            _currentChunkData.Span.Slice(chunkOffset, numToRead).CopyTo(buffer.AsSpan(offset + totalRead, numToRead));

            _position += numToRead;
            totalRead += numToRead;
        }

        return totalRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
#if NET7_0_OR_GREATER
        ObjectDisposedException.ThrowIf(IsDisposed, this);
#else
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(FileResourceStream));
        }
#endif

        if (_position >= Length)
        {
            return 0;
        }

        var maxToRead = (int)Math.Min(Length - _position, buffer.Length);

        var totalRead = 0;

        while (totalRead < maxToRead)
        {
            var chunk = (int)(_position / _chunkSize);
            var chunkOffset = (int)(_position % _chunkSize);
            var numToRead = Math.Min(maxToRead - totalRead, _chunkSize - chunkOffset);

            if (numToRead == 0)
            {
                return totalRead;
            }

            if (_currentChunk != chunk || _currentChunkData.IsEmpty)
            {
                _chunkBuffer ??= ArrayPool<byte>.Shared.Rent(_chunkSize);
                _currentChunkData = new(_chunkBuffer, 0, await DecompressChunkAsync(chunk, _chunkBuffer, cancellationToken).ConfigureAwait(false));
                _currentChunk = chunk;
            }

            _currentChunkData.Slice(chunkOffset, numToRead).CopyTo(buffer.Slice(totalRead, numToRead));

            _position += numToRead;
            totalRead += numToRead;
        }

        return totalRead;
    }

    public override int Read(Span<byte> buffer)
    {
#if NET7_0_OR_GREATER
        ObjectDisposedException.ThrowIf(IsDisposed, this);
#else
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(FileResourceStream));
        }
#endif

        if (_position >= Length)
        {
            return 0;
        }

        var maxToRead = (int)Math.Min(Length - _position, buffer.Length);

        var totalRead = 0;

        while (totalRead < maxToRead)
        {
            var chunk = (int)(_position / _chunkSize);
            var chunkOffset = (int)(_position % _chunkSize);
            var numToRead = Math.Min(maxToRead - totalRead, _chunkSize - chunkOffset);

            if (numToRead == 0)
            {
                return totalRead;
            }

            if (_currentChunk != chunk || _currentChunkData.IsEmpty)
            {
                _chunkBuffer ??= ArrayPool<byte>.Shared.Rent(_chunkSize);
                _currentChunkData = new(_chunkBuffer, 0, DecompressChunk(chunk, _chunkBuffer));
                _currentChunk = chunk;
            }

            _currentChunkData.Span.Slice(chunkOffset, numToRead).CopyTo(buffer.Slice(totalRead, numToRead));

            _position += numToRead;
            totalRead += numToRead;
        }

        return totalRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    private int DecompressChunk(int chunk, Memory<byte> buffer)
    {
        var targetUncompressed = _chunkSize;
        if (chunk == _chunkLength.Length - 1)
        {
            targetUncompressed = (int)(Length - _position);
        }

        var compressedSize = (int)_chunkLength[chunk];

        _baseStream.Position = _offsetDelta + _chunkOffsets[chunk];

        if (_blockDecompressor is null
            || (_header.Flags & ResourceFlags.Compressed) == 0 || _chunkLength[chunk] == targetUncompressed)
        {
            return _baseStream.ReadMaximum(buffer.Span.Slice(0, compressedSize));
        }

        var rawChunk = ArrayPool<byte>.Shared.Rent(compressedSize);
        try
        {
            _baseStream.ReadExactly(rawChunk.AsSpan(0, compressedSize));

            var bufferSpan = buffer.Span;

            bufferSpan.Clear();

            if (!_blockDecompressor.TryDecompress(rawChunk.AsSpan(0, compressedSize), bufferSpan.Slice(0, targetUncompressed), out var bytesWritten))
            {
                throw new IOException($"Failed to decompress chunk {chunk} in resource at location {_header.FileOffset}");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rawChunk);
        }

        return targetUncompressed;
    }

    private async ValueTask<int> DecompressChunkAsync(int chunk, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var targetUncompressed = _chunkSize;
        if (chunk == _chunkLength.Length - 1)
        {
            targetUncompressed = (int)(Length - _position);
        }

        var compressedSize = (int)_chunkLength[chunk];

        _baseStream.Position = _offsetDelta + _chunkOffsets[chunk];

        if (_blockDecompressor is null
            || (_header.Flags & ResourceFlags.Compressed) == 0 || _chunkLength[chunk] == targetUncompressed)
        {
            return await _baseStream.ReadMaximumAsync(buffer.Slice(0, compressedSize), cancellationToken).ConfigureAwait(false);
        }

        var rawChunk = ArrayPool<byte>.Shared.Rent(compressedSize);
        try
        {
            await _baseStream.ReadExactlyAsync(rawChunk.AsMemory(0, compressedSize), cancellationToken).ConfigureAwait(false);

            if (!_blockDecompressor.TryDecompress(rawChunk.AsSpan(0, compressedSize), buffer.Span.Slice(0, targetUncompressed), out var bytesWritten))
            {
                throw new IOException($"Failed to decompress chunk {chunk} in resource at location {_header.FileOffset}");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rawChunk);
        }

        return targetUncompressed;
    }
}