using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using DiscUtils.Streams;
using Microsoft.Win32.SafeHandles;

namespace DiscUtils.Windows;

/// <summary>
/// A read-only stream over a Windows raw device handle. Raw device handles only accept reads that start and end on sector boundaries into a
/// sector-aligned buffer, so each read is widened to whole sectors through an aligned buffer of its own and the requested bytes are copied out.
/// </summary>
public class DeviceStream : SparseStream.ReadOnlySparseStream
{
    private const int BufferSize = 1 << 20;

    private readonly SafeFileHandle _handle;
    private readonly bool _ownsHandle;
    private readonly int _alignment;
    private IntPtr _bufferAllocation;
    private readonly IntPtr _buffer;
    private long _position;

    /// <param name="handle">An open device handle.</param>
    /// <param name="length">The device's length in bytes.</param>
    /// <param name="sectorSize">The sector size: reads are aligned to it (a power of two, at least 512).</param>
    /// <param name="ownsHandle">Whether disposing the stream closes the handle.</param>
    public DeviceStream(SafeFileHandle handle, long length, int sectorSize, bool ownsHandle)
    {
        if (sectorSize < 512 || (sectorSize & (sectorSize - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sectorSize), sectorSize, "The sector size must be a power of two of at least 512.");
        }

        _handle = handle;
        _ownsHandle = ownsHandle;
        _alignment = Math.Max(sectorSize, 4096);
        Length = length;
        _bufferAllocation = Marshal.AllocHGlobal(BufferSize + _alignment);
        _buffer = new IntPtr((_bufferAllocation.ToInt64() + _alignment - 1) / _alignment * _alignment);
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override long Length { get; }

    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    public override IEnumerable<StreamExtent> Extents => [new StreamExtent(0, Length)];

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override unsafe int Read(Span<byte> buffer)
    {
        var total = 0;

        while (total < buffer.Length && _position < Length)
        {
            var alignedStart = _position / _alignment * _alignment;
            var skip = (int)(_position - alignedStart);
            var wanted = (int)Math.Min(BufferSize, (Length - alignedStart + _alignment - 1) / _alignment * _alignment);

            if (!NativeMethods.SetFilePointerEx(_handle, alignedStart, out _, 0))
            {
                throw new IOException($"Seek to {alignedStart} failed.", new Win32Exception());
            }

            if (!NativeMethods.ReadFile(_handle, _buffer, wanted, out var read, IntPtr.Zero))
            {
                throw new IOException($"Read of {wanted} bytes at {alignedStart} failed.", new Win32Exception());
            }

            var useful = (int)Math.Min(read - skip, Length - _position);
            if (useful <= 0)
            {
                break;
            }

            var count = Math.Min(buffer.Length - total, useful);
            new ReadOnlySpan<byte>((byte*)_buffer + skip, count).CopyTo(buffer.Slice(total));
            total += count;
            _position += count;
        }

        return total;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => offset
        };

        if (target < 0)
        {
            throw new IOException("Attempt to move before the beginning of the device.");
        }

        _position = target;
        return _position;
    }

    protected override void Dispose(bool disposing)
    {
        if (_bufferAllocation != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_bufferAllocation);
            _bufferAllocation = IntPtr.Zero;
        }

        if (disposing && _ownsHandle)
        {
            _handle.Dispose();
        }

        base.Dispose(disposing);
    }
}
