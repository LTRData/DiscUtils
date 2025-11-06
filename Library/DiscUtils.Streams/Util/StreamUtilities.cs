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

using DiscUtils.Streams.Compatibility;
using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DiscUtils.Streams;

public static class StreamUtilities
{
    public static T[] GetUninitializedArray<T>(int length) where T : unmanaged
    {
        if (length == 0)
        {
            return [];
        }

#if NET5_0_OR_GREATER
        return GC.AllocateUninitializedArray<T>(length);
#else
        return new T[length];
#endif
    }

    /// <summary>
    /// Validates standard buffer, offset, count parameters to a method.
    /// </summary>
    /// <param name="buffer">The byte array to read from / write to.</param>
    /// <param name="offset">The starting offset in <c>buffer</c>.</param>
    /// <param name="count">The number of bytes to read / write.</param>
    public static void AssertBufferParameters(byte[] buffer, int offset, int count)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfLessThan(buffer.Length, offset + count);
#else
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Offset is negative");
        }

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count is negative");
        }

        if (buffer.Length < offset + count)
        {
            throw new ArgumentException("buffer is too small", nameof(buffer));
        }
#endif
    }

    #region Stream Manipulation

#if !NET7_0_OR_GREATER

    /// <summary>
    /// Read bytes until buffer filled or throw EndOfStreamException.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="buffer">The buffer to populate.</param>
    /// <param name="offset">Offset in the buffer to start.</param>
    /// <param name="count">The number of bytes to read.</param>
    public static void ReadExactly(this Stream stream, byte[] buffer, int offset, int count)
    {
        var originalCount = count;

        while (count > 0)
        {
            var numRead = stream.Read(buffer, offset, count);

            if (numRead == 0)
            {
                throw new EndOfStreamException($"Unable to complete read of {originalCount} bytes");
            }

            offset += numRead;
            count -= numRead;
        }
    }

    /// <summary>
    /// Read bytes until buffer filled or throw EndOfStreamException.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="buffer">The buffer to populate.</param>
    /// <param name="cancellationToken"></param>
    public static async ValueTask ReadExactlyAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var originalCount = buffer.Length;

        while (buffer.Length > 0)
        {
            var numRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

            if (numRead == 0)
            {
                throw new EndOfStreamException($"Unable to complete read of {originalCount} bytes");
            }

            buffer = buffer.Slice(numRead);
        }
    }

    /// <summary>
    /// Read bytes until buffer filled or throw EndOfStreamException.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="buffer">The buffer to populate.</param>
    public static void ReadExactly(this Stream stream, Span<byte> buffer)
    {
        var originalCount = buffer.Length;

        while (buffer.Length > 0)
        {
            var numRead = stream.Read(buffer);

            if (numRead == 0)
            {
                throw new EndOfStreamException($"Unable to complete read of {originalCount} bytes");
            }

            buffer = buffer.Slice(numRead);
        }
    }

#endif

    /// <summary>
    /// Read bytes until buffer filled or throw EndOfStreamException.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>The data read from the stream.</returns>
    public static byte[] ReadExactly(this Stream stream, int count)
    {
        var buffer = GetUninitializedArray<byte>(count);

        stream.ReadExactly(buffer, 0, count);

        return buffer;
    }

    /// <summary>
    /// Read bytes until buffer filled or throw EndOfStreamException.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="count">The number of bytes to read.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The data read from the stream.</returns>
    public static async ValueTask<byte[]> ReadExactlyAsync(this Stream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = GetUninitializedArray<byte>(count);

        await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);

        return buffer;
    }

    /// <summary>
    /// Read bytes until buffer filled or throw EndOfStreamException.
    /// </summary>
    /// <param name="buffer">The stream to read.</param>
    /// <param name="pos">The position in buffer to read from.</param>
    /// <param name="data">The buffer to populate.</param>
    /// <param name="offset">Offset in the buffer to start.</param>
    /// <param name="count">The number of bytes to read.</param>
    public static void ReadExactly(this IBuffer buffer, long pos, byte[] data, int offset, int count)
    {
        var originalCount = count;

        while (count > 0)
        {
            var numRead = buffer.Read(pos, data, offset, count);

            if (numRead == 0)
            {
                throw new EndOfStreamException($"Unable to complete read of {originalCount} bytes");
            }

            pos += numRead;
            offset += numRead;
            count -= numRead;
        }
    }

    /// <summary>
    /// Read bytes until buffer filled or throw EndOfStreamException.
    /// </summary>
    /// <param name="buffer">The stream to read.</param>
    /// <param name="pos">The position in buffer to read from.</param>
    /// <param name="data">The buffer to populate.</param>
    /// <param name="cancellationToken"></param>
    public static async ValueTask ReadExactlyAsync(this IBuffer buffer, long pos, Memory<byte> data, CancellationToken cancellationToken)
    {
        var originalCount = data.Length;

        while (data.Length > 0)
        {
            var numRead = await buffer.ReadAsync(pos, data, cancellationToken).ConfigureAwait(false);

            if (numRead == 0)
            {
                throw new EndOfStreamException($"Unable to complete read of {originalCount} bytes");
            }

            pos += numRead;
            data = data.Slice(numRead);
        }
    }

    /// <summary>
    /// Read bytes until buffer filled or throw EndOfStreamException.
    /// </summary>
    /// <param name="buffer">The stream to read.</param>
    /// <param name="pos">The position in buffer to read from.</param>
    /// <param name="data">The buffer to populate.</param>
    public static void ReadExactly(this IBuffer buffer, long pos, Span<byte> data)
    {
        var originalCount = data.Length;

        while (data.Length > 0)
        {
            var numRead = buffer.Read(pos, data);

            if (numRead == 0)
            {
                throw new EndOfStreamException($"Unable to complete read of {originalCount} bytes");
            }

            pos += numRead;
            data = data.Slice(numRead);
        }
    }

    /// <summary>
    /// Read bytes until buffer filled or throw EndOfStreamException.
    /// </summary>
    /// <param name="buffer">The buffer to read.</param>
    /// <param name="pos">The position in buffer to read from.</param>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>The data read from the stream.</returns>
    public static byte[] ReadExactly(this IBuffer buffer, long pos, int count)
    {
        var result = GetUninitializedArray<byte>(count);

        ReadExactly(buffer, pos, result, 0, count);

        return result;
    }

    /// <summary>
    /// Read bytes until buffer filled or throw EndOfStreamException.
    /// </summary>
    /// <param name="buffer">The buffer to read.</param>
    /// <param name="pos">The position in buffer to read from.</param>
    /// <param name="count">The number of bytes to read.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The data read from the stream.</returns>
    public static async ValueTask<byte[]> ReadExactlyAsync(this IBuffer buffer, long pos, int count, CancellationToken cancellationToken)
    {
        var result = GetUninitializedArray<byte>(count);

        await ReadExactlyAsync(buffer, pos, result, cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Read bytes until buffer filled or EOF.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="buffer">The buffer to populate.</param>
    /// <param name="offset">Offset in the buffer to start.</param>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>The number of bytes actually read.</returns>
#if NET7_0_OR_GREATER
    public static int ReadMaximum(this Stream stream, byte[] buffer, int offset, int count)
        => stream.ReadAtLeast(buffer.AsSpan(offset, count), count, throwOnEndOfStream: false);
#else
    public static int ReadMaximum(this Stream stream, byte[] buffer, int offset, int count)
    {
        var totalRead = 0;

        while (count > 0)
        {
            var numRead = stream.Read(buffer, offset, count);

            if (numRead == 0)
            {
                return totalRead;
            }

            offset += numRead;
            count -= numRead;
            totalRead += numRead;
        }

        return totalRead;
    }
#endif

    /// <summary>
    /// Read bytes until buffer filled or EOF.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="buffer">The buffer to populate.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The number of bytes actually read.</returns>
#if NET7_0_OR_GREATER
    public static ValueTask<int> ReadMaximumAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
        => stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
#else
    public static async ValueTask<int> ReadMaximumAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var totalRead = 0;

        while (buffer.Length > 0)
        {
            var numRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

            if (numRead == 0)
            {
                return totalRead;
            }

            buffer = buffer.Slice(numRead);
            totalRead += numRead;
        }

        return totalRead;
    }
#endif

    /// <summary>
    /// Read bytes until buffer filled or EOF.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="buffer">The buffer to populate.</param>
    /// <returns>The number of bytes actually read.</returns>
#if NET7_0_OR_GREATER
    public static int ReadMaximum(this Stream stream, Span<byte> buffer)
        => stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
#else
    public static int ReadMaximum(this Stream stream, Span<byte> buffer)
    {
        var totalRead = 0;

        while (buffer.Length > 0)
        {
            var numRead = stream.Read(buffer);

            if (numRead == 0)
            {
                return totalRead;
            }

            buffer = buffer.Slice(numRead);
            totalRead += numRead;
        }

        return totalRead;
    }
#endif

    /// <summary>
    /// Read bytes until buffer filled or EOF.
    /// </summary>
    /// <param name="buffer">The stream to read.</param>
    /// <param name="pos">The position in buffer to read from.</param>
    /// <param name="data">The buffer to populate.</param>
    /// <param name="offset">Offset in the buffer to start.</param>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>The number of bytes actually read.</returns>
    public static int ReadMaximum(this IBuffer buffer, long pos, byte[] data, int offset, int count)
    {
        var totalRead = 0;

        while (count > 0)
        {
            var numRead = buffer.Read(pos, data, offset, count);

            if (numRead == 0)
            {
                return totalRead;
            }

            pos += numRead;
            offset += numRead;
            count -= numRead;
            totalRead += numRead;
        }

        return totalRead;
    }

    /// <summary>
    /// Read bytes until buffer filled or EOF.
    /// </summary>
    /// <param name="buffer">The stream to read.</param>
    /// <param name="pos">The position in buffer to read from.</param>
    /// <param name="data">The buffer to populate.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The number of bytes actually read.</returns>
    public static async ValueTask<int> ReadMaximumAsync(this IBuffer buffer, long pos, Memory<byte> data, CancellationToken cancellationToken)
    {
        var totalRead = 0;

        while (data.Length > 0)
        {
            var numRead = await buffer.ReadAsync(pos, data, cancellationToken).ConfigureAwait(false);

            if (numRead == 0)
            {
                return totalRead;
            }

            pos += numRead;
            data = data.Slice(numRead);
            totalRead += numRead;
        }

        return totalRead;
    }

    /// <summary>
    /// Read bytes until buffer filled or EOF.
    /// </summary>
    /// <param name="buffer">The stream to read.</param>
    /// <param name="pos">The position in buffer to read from.</param>
    /// <param name="data">The buffer to populate.</param>
    /// <returns>The number of bytes actually read.</returns>
    public static int ReadMaximum(this IBuffer buffer, long pos, Span<byte> data)
    {
        var totalRead = 0;

        while (data.Length > 0)
        {
            var numRead = buffer.Read(pos, data);

            if (numRead == 0)
            {
                return totalRead;
            }

            pos += numRead;
            data = data.Slice(numRead);
            totalRead += numRead;
        }

        return totalRead;
    }

    /// <summary>
    /// Read bytes until buffer filled or throw EndOfStreamException.
    /// </summary>
    /// <param name="buffer">The buffer to read.</param>
    /// <returns>The data read from the stream.</returns>
    public static byte[] ReadAll(this IBuffer buffer)
    {
        return ReadExactly(buffer, 0, (int)buffer.Capacity);
    }

    /// <summary>
    /// Read bytes until buffer filled or throw EndOfStreamException.
    /// </summary>
    /// <param name="buffer">The buffer to read.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The data read from the stream.</returns>
    public static ValueTask<byte[]> ReadAllAsync(this IBuffer buffer, CancellationToken cancellationToken)
    {
        return ReadExactlyAsync(buffer, 0, (int)buffer.Capacity, cancellationToken);
    }

    /// <summary>
    /// Reads remaining data of a stream from current position to end of stream
    /// into a new byte array. Booth seeking and nonseeking streams are supported.
    /// </summary>
    /// <param name="stream">Stream to read from</param>
    /// <returns>Byte array with data read from <paramref name="stream"/></returns>
    public static byte[] ReadToEnd(this Stream stream)
    {
        if (stream.CanSeek)
        {
            return stream.ReadExactly((int)(stream.Length - stream.Position));
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Reads remaining data of a stream from current position to end of stream
    /// into a new byte array. Booth seeking and nonseeking streams are supported.
    /// </summary>
    /// <param name="stream">Stream to read from</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Byte array with data read from <paramref name="stream"/></returns>
    public static async Task<byte[]> ReadToEndAsync(this Stream stream, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
        {
            return await stream.ReadExactlyAsync((int)(stream.Length - stream.Position), cancellationToken).ConfigureAwait(false);
        }

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        return ms.ToArray();
    }

    /// <summary>
    /// Reads a disk sector (512 bytes).
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <returns>The sector data as a byte array.</returns>
    public static byte[] ReadSector(this Stream stream)
    {
        return ReadExactly(stream, Sizes.Sector);
    }

    /// <summary>
    /// Reads a disk sector (512 bytes).
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The sector data as a byte array.</returns>
    public static ValueTask<byte[]> ReadSectorAsync(this Stream stream, CancellationToken cancellationToken)
    {
        return ReadExactlyAsync(stream, Sizes.Sector, cancellationToken);
    }

    /// <summary>
    /// Reads a structure from a stream.
    /// </summary>
    /// <typeparam name="T">The type of the structure.</typeparam>
    /// <param name="stream">The stream to read.</param>
    /// <returns>The structure.</returns>
    public static T ReadStruct<T>(this Stream stream)
        where T : IByteArraySerializable, new()
    {
        var result = new T();

        byte[]? allocated = null;

        var buffer = result.Size <= 1024
            ? stackalloc byte[result.Size]
            : (allocated = ArrayPool<byte>.Shared.Rent(result.Size)).AsSpan(0, result.Size);

        try
        {
            stream.ReadExactly(buffer);
            result.ReadFrom(buffer);
        }
        finally
        {
            if (allocated is not null)
            {
                ArrayPool<byte>.Shared.Return(allocated);
            }
        }

        return result;
    }

    /// <summary>
    /// Reads a structure from a stream.
    /// </summary>
    /// <typeparam name="T">The type of the structure.</typeparam>
    /// <param name="stream">The stream to read.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The structure.</returns>
    public static async ValueTask<T> ReadStructAsync<T>(this Stream stream, CancellationToken cancellationToken)
        where T : IByteArraySerializable, new()
    {
        var result = new T();
        var buffer = ArrayPool<byte>.Shared.Rent(result.Size);
        try
        {
            var mem = buffer.AsMemory(0, result.Size);
            await stream.ReadExactlyAsync(mem, cancellationToken).ConfigureAwait(false);
            result.ReadFrom(mem.Span);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return result;
    }

    /// <summary>
    /// Reads a structure from a stream.
    /// </summary>
    /// <typeparam name="T">The type of the structure.</typeparam>
    /// <param name="stream">The stream to read.</param>
    /// <param name="length">Number of bytes to read</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The structure.</returns>
    public static async ValueTask<T> ReadStructAsync<T>(this Stream stream, int length, CancellationToken cancellationToken)
        where T : IByteArraySerializable, new()
    {
        var result = new T();
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            var mem = buffer.AsMemory(0, length);
            await stream.ReadExactlyAsync(mem, cancellationToken).ConfigureAwait(false);
            result.ReadFrom(mem.Span);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return result;
    }

    /// <summary>
    /// Reads a structure from a stream into an existing instance.
    /// </summary>
    /// <typeparam name="T">The type of the structure.</typeparam>
    /// <param name="result">Existing instance</param>
    /// <param name="stream">The stream to read.</param>
    /// <returns>The structure.</returns>
    public static void ReadFrom<T>(this T result, Stream stream)
        where T : class, IByteArraySerializable
    {
        byte[]? allocated = null;

        var buffer = result.Size <= 1024
            ? stackalloc byte[result.Size]
            : (allocated = ArrayPool<byte>.Shared.Rent(result.Size)).AsSpan(0, result.Size);

        try
        {
            stream.ReadExactly(buffer);
            result.ReadFrom(buffer);
        }
        finally
        {
            if (allocated is not null)
            {
                ArrayPool<byte>.Shared.Return(allocated);
            }
        }
    }

    /// <summary>
    /// Reads a structure from a stream into an existing instance.
    /// </summary>
    /// <typeparam name="T">The type of the structure.</typeparam>
    /// <param name="result">Existing instance</param>
    /// <param name="stream">The stream to read.</param>
    /// <param name="length">Number of bytes to read from stream</param>
    /// <returns>The structure.</returns>
    public static void ReadFrom<T>(this T result, Stream stream, int length)
        where T : class, IByteArraySerializable
    {
        byte[]? allocated = null;

        var buffer = length <= 1024
            ? stackalloc byte[length]
            : (allocated = ArrayPool<byte>.Shared.Rent(length)).AsSpan(0, length);

        try
        {
            stream.ReadExactly(buffer);
            result.ReadFrom(buffer);
        }
        finally
        {
            if (allocated is not null)
            {
                ArrayPool<byte>.Shared.Return(allocated);
            }
        }
    }

    /// <summary>
    /// Reads a structure from a stream into an existing instance.
    /// </summary>
    /// <typeparam name="T">The type of the structure.</typeparam>
    /// <param name="result">Existing instance</param>
    /// <param name="stream">The stream to read.</param>
    /// <returns>The structure.</returns>
    public static void ReadFrom<T>(this ref T result, Stream stream)
        where T : struct, IByteArraySerializable
    {
        byte[]? allocated = null;

        var buffer = result.Size <= 1024
            ? stackalloc byte[result.Size]
            : (allocated = ArrayPool<byte>.Shared.Rent(result.Size)).AsSpan(0, result.Size);

        try
        {
            stream.ReadExactly(buffer);
            result.ReadFrom(buffer);
        }
        finally
        {
            if (allocated is not null)
            {
                ArrayPool<byte>.Shared.Return(allocated);
            }
        }
    }

    /// <summary>
    /// Reads a structure from a stream into an existing instance.
    /// </summary>
    /// <typeparam name="T">The type of the structure.</typeparam>
    /// <param name="result">Existing instance</param>
    /// <param name="stream">The stream to read.</param>
    /// <param name="length">Number of bytes to read from stream</param>
    /// <returns>The structure.</returns>
    public static void ReadFrom<T>(this ref T result, Stream stream, int length)
        where T : struct, IByteArraySerializable
    {
        byte[]? allocated = null;

        var buffer = length <= 1024
            ? stackalloc byte[length]
            : (allocated = ArrayPool<byte>.Shared.Rent(length)).AsSpan(0, length);

        try
        {
            stream.ReadExactly(buffer);
            result.ReadFrom(buffer);
        }
        finally
        {
            if (allocated is not null)
            {
                ArrayPool<byte>.Shared.Return(allocated);
            }
        }
    }

    /// <summary>
    /// Reads a structure from a stream.
    /// </summary>
    /// <typeparam name="T">The type of the structure.</typeparam>
    /// <param name="stream">The stream to read.</param>
    /// <param name="length">The number of bytes to read.</param>
    /// <returns>The structure.</returns>
    public static T ReadStruct<T>(this Stream stream, int length)
        where T : IByteArraySerializable, new()
    {
        byte[]? allocated = null;

        var buffer = length <= 1024
            ? stackalloc byte[length]
            : (allocated = ArrayPool<byte>.Shared.Rent(length)).AsSpan(0, length);

        try
        {
            stream.ReadExactly(buffer);
            var result = new T();
            result.ReadFrom(buffer);
            return result;
        }
        finally
        {
            if (allocated is not null)
            {
                ArrayPool<byte>.Shared.Return(allocated);
            }
        }
    }

    /// <summary>
    /// Writes a structure to a stream.
    /// </summary>
    /// <typeparam name="T">The type of the structure.</typeparam>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="obj">The structure to write.</param>
    public static void WriteStruct<T>(this Stream stream, T obj)
        where T : IByteArraySerializable
    {
        byte[]? allocated = null;

        var objSize = obj.Size;

        var buffer = objSize <= 1024
            ? stackalloc byte[objSize]
            : (allocated = ArrayPool<byte>.Shared.Rent(objSize)).AsSpan(0, objSize);

        try
        {
            obj.WriteTo(buffer);
            stream.Write(buffer);
        }
        finally
        {
            if (allocated is not null)
            {
                ArrayPool<byte>.Shared.Return(allocated);
            }
        }
    }

    /// <summary>
    /// Writes a structure to a stream.
    /// </summary>
    /// <typeparam name="T">The type of the structure.</typeparam>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="obj">The structure to write.</param>
    /// <param name="cancellationToken"></param>
    public static async ValueTask WriteStructAsync<T>(this Stream stream, T obj, CancellationToken cancellationToken)
        where T : IByteArraySerializable
    {
        var objSize = obj.Size;

        var buffer = ArrayPool<byte>.Shared.Rent(objSize);

        try
        {
            obj.WriteTo(buffer);
            await stream.WriteAsync(buffer.AsMemory(0, objSize), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    #endregion
}
