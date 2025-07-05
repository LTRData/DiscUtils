using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace DiscUtils.Streams.Compatibility;

public static class CompatExtensions
{
    public static int ReadFrom<T>(this T serializable, byte[] bytes, int offset) where T : class, IByteArraySerializable =>
        serializable.ReadFrom(bytes.AsSpan(offset));

    public static void WriteTo<T>(this T serializable, byte[] bytes, int offset) where T : class, IByteArraySerializable =>
        serializable.WriteTo(bytes.AsSpan(offset));

    public static int ReadFrom<T>(ref this T serializable, byte[] bytes, int offset) where T : struct, IByteArraySerializable =>
        serializable.ReadFrom(bytes.AsSpan(offset));

    public static void WriteTo<T>(ref this T serializable, byte[] bytes, int offset) where T : struct, IByteArraySerializable =>
        serializable.WriteTo(bytes.AsSpan(offset));

#if !NETSTANDARD2_1_OR_GREATER && !NETCOREAPP

    public static void NextBytes(this Random random, Span<byte> buffer)
    {
        var bytes = ArrayPool<byte>.Shared.Rent(buffer.Length);

        try
        {
            random.NextBytes(bytes);
            bytes.AsSpan(0, buffer.Length).CopyTo(buffer);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    public static int Read(this Stream stream, Span<byte> buffer)
    {
        if (stream is CompatibilityStream compatibilityStream)
        {
            return compatibilityStream.Read(buffer);
        }

        return ReadUsingArray(stream, buffer);
    }

    public static int ReadUsingArray(Stream stream, Span<byte> buffer)
    {
        var bytes = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            var numRead = stream.Read(bytes, 0, buffer.Length);
            bytes.AsSpan(0, numRead).CopyTo(buffer);
            return numRead;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    public static ValueTask<int> ReadAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (stream is CompatibilityStream compatibilityStream)
        {
            return compatibilityStream.ReadAsync(buffer, cancellationToken);
        }

        return ReadUsingArrayAsync(stream, buffer, cancellationToken);
    }

    public static ValueTask<int> ReadUsingArrayAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (MemoryMarshal.TryGetArray<byte>(buffer, out var arraySegment))
        {
            return new(stream.ReadAsync(arraySegment.Array, arraySegment.Offset, arraySegment.Count, cancellationToken));
        }

        return ReadUsingTemporaryArrayAsync(stream, buffer, cancellationToken);
    }

    private static async ValueTask<int> ReadUsingTemporaryArrayAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var bytes = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            var numRead = await stream.ReadAsync(bytes, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            bytes.AsSpan(0, numRead).CopyTo(buffer.Span);
            return numRead;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    public static void Write(this Stream stream, ReadOnlySpan<byte> buffer)
    {
        if (stream is CompatibilityStream compatibilityStream)
        {
            compatibilityStream.Write(buffer);
            return;
        }

        var bytes = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            buffer.CopyTo(bytes);
            stream.Write(bytes, 0, buffer.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    public static ValueTask WriteAsync(this Stream stream, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (stream is CompatibilityStream compatibilityStream)
        {
            return compatibilityStream.WriteAsync(buffer, cancellationToken);
        }

        if (MemoryMarshal.TryGetArray(buffer, out var arraySegment))
        {
            return new(stream.WriteAsync(arraySegment.Array, arraySegment.Offset, arraySegment.Count, cancellationToken));
        }

        return WriteUsingTemporaryArrayAsync(stream, buffer, cancellationToken);
    }

    private static async ValueTask WriteUsingTemporaryArrayAsync(Stream stream, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var bytes = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            buffer.CopyTo(bytes);
            await stream.WriteAsync(bytes, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    public static void AppendData(this IncrementalHash hash, ReadOnlySpan<byte> data)
    {
        var bytes = ArrayPool<byte>.Shared.Rent(data.Length);
        try
        {
            data.CopyTo(bytes);
            hash.AppendData(bytes, 0, data.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    public static Task CopyToAsync(this Stream source, Stream target, CancellationToken cancellationToken)
        => source.CopyToAsync(target, bufferSize: 80 * 1024, cancellationToken);
#endif

    /// <summary>
    /// If stream is derived from CompatibilityStream, get corresponding position in a base stream of this instance
    /// to a position in this instance.
    /// </summary>
    public static long? GetPositionInBaseStream(this Stream stream, Stream baseStream, long virtualPosition)
    {
        if (ReferenceEquals(stream, baseStream))
        {
            return virtualPosition;
        }

        if (stream is CompatibilityStream compatBaseStream)
        {
            return compatBaseStream.GetPositionInBaseStream(baseStream, virtualPosition);
        }

        if (baseStream is null)
        {
            return virtualPosition;
        }

        return null;
    }
}

