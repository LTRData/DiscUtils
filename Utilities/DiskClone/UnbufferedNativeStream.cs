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
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using DiscUtils.Streams;
using LTRData.Extensions.Buffers;
using Microsoft.Win32.SafeHandles;

namespace DiskClone;

/// <summary>
/// A stream implementation that honours the alignment rules for unbuffered streams.
/// </summary>
/// <remarks>
/// To support the stream interface, which permits unaligned access, all accesses
/// are routed through an appropriately aligned buffer.
/// </remarks>
public class UnbufferedNativeStream : SparseStream.ReadOnlySparseStream
{
    private const int BufferSize = 64 * 1024;
    private const int Alignment = 512;

    private long _position;
    private SafeFileHandle _handle;
    private IntPtr _bufferAllocHandle;
    private IntPtr _buffer;

    public UnbufferedNativeStream(SafeFileHandle handle)
    {
        _handle = handle;

        _bufferAllocHandle = Marshal.AllocHGlobal(BufferSize + Alignment);
        _buffer = new IntPtr(((_bufferAllocHandle.ToInt64() + Alignment - 1) / Alignment) * Alignment);

        _position = 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (_bufferAllocHandle != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_bufferAllocHandle);
            _bufferAllocHandle = IntPtr.Zero;
            _bufferAllocHandle = IntPtr.Zero;
        }

        if (!_handle.IsClosed)
        {
            _handle.Close();
        }

        base.Dispose(disposing);
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override long Length
    {
        get
        {
            if (NativeMethods.GetFileSizeEx(_handle, out var result))
            {
                return result;
            }
            else
            {
                throw new Win32Exception();
            }
        }
    }

    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override unsafe int Read(Span<byte> buffer)
    {
        var totalBytesRead = 0;
        var length = Length;

        while (totalBytesRead < buffer.Length)
        {
            var alignedStart = (_position / Alignment) * Alignment;
            var alignmentOffset = (int)(_position - alignedStart);

            if (!NativeMethods.SetFilePointerEx(_handle, alignedStart, out var newPos, 0))
            {
                throw new Win32Exception();
            }

            var toRead = (int)Math.Min(length - alignedStart, BufferSize);
            if (!NativeMethods.ReadFile(_handle, _buffer, toRead, out var numRead, IntPtr.Zero))
            {
                throw new Win32Exception();
            }

            var usefulData = numRead - alignmentOffset;
            if (usefulData <= 0)
            {
                return totalBytesRead;
            }

            var toCopy = Math.Min(buffer.Length - totalBytesRead, usefulData);

            new ReadOnlySpan<byte>((_buffer + alignmentOffset).ToPointer(), toCopy).CopyTo(buffer.Slice(totalBytesRead));

            totalBytesRead += toCopy;
            _position += toCopy;
        }

        return totalBytesRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var effectiveOffset = offset;
        if (origin == SeekOrigin.Current)
        {
            effectiveOffset += _position;
        }
        else if (origin == SeekOrigin.End)
        {
            effectiveOffset += Length;
        }

        if (effectiveOffset < 0)
        {
            throw new IOException("Attempt to move before beginning of disk");
        }
        else
        {
            _position = effectiveOffset;
            return _position;
        }
    }

    public override IEnumerable<StreamExtent> Extents
        => SingleValueEnumerable.Get(new StreamExtent(0, Length));
}
