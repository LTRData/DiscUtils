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

using DiscUtils.Streams;
using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DiscUtils.Iscsi;

internal class ProtocolDataUnit
{
    public ProtocolDataUnit(byte[] headerData, byte[] contentData)
    {
        HeaderData = headerData;
        ContentData = contentData;
    }

    public byte[] ContentData { get; }

    public byte[] HeaderData { get; }

    public OpCode OpCode => (OpCode)(HeaderData[0] & 0x3F);

    public static ProtocolDataUnit ReadFrom(Stream stream, bool headerDigestEnabled, bool dataDigestEnabled)
    {
        var numRead = 0;

        var headerData = stream.ReadExactly(48);
        numRead += 48;

        byte[] contentData = null;

        if (headerDigestEnabled)
        {
            var digest = ReadDigest(stream);
            numRead += 4;
        }

        var bhs = new BasicHeaderSegment();
        bhs.ReadFrom(headerData);

        if (bhs.DataSegmentLength > 0)
        {
            contentData = stream.ReadExactly(bhs.DataSegmentLength);
            numRead += bhs.DataSegmentLength;

            if (dataDigestEnabled)
            {
                var digest = ReadDigest(stream);
                numRead += 4;
            }
        }

        var rem = 4 - numRead % 4;
        if (rem != 4)
        {
            stream.ReadExactly(stackalloc byte[rem]);
        }

        return new ProtocolDataUnit(headerData, contentData);
    }

    private static uint ReadDigest(Stream stream)
    {
        Span<byte> data = stackalloc byte[4];
        stream.ReadExactly(data);
        return EndianUtilities.ToUInt32BigEndian(data);
    }

    public static async ValueTask<ProtocolDataUnit> ReadFromAsync(Stream stream, bool headerDigestEnabled, bool dataDigestEnabled, CancellationToken cancellationToken)
    {
        var numRead = 0;

        var headerData = await stream.ReadExactlyAsync(48, cancellationToken).ConfigureAwait(false);
        numRead += 48;

        byte[] contentData = null;

        if (headerDigestEnabled)
        {
            var digest = ReadDigest(stream);
            numRead += 4;
        }

        var bhs = new BasicHeaderSegment();
        bhs.ReadFrom(headerData);

        if (bhs.DataSegmentLength > 0)
        {
            contentData = await stream.ReadExactlyAsync(bhs.DataSegmentLength, cancellationToken).ConfigureAwait(false);
            numRead += bhs.DataSegmentLength;

            if (dataDigestEnabled)
            {
                var digest = await ReadDigestAsync(stream, cancellationToken).ConfigureAwait(false);
                numRead += 4;
            }
        }

        var rem = 4 - numRead % 4;
        if (rem != 4)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(rem);
            try
            {
                await stream.ReadExactlyAsync(buffer.AsMemory(0, rem), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        return new ProtocolDataUnit(headerData, contentData);
    }

    private static async ValueTask<uint> ReadDigestAsync(Stream stream, CancellationToken cancellationToken)
    {
        var data = ArrayPool<byte>.Shared.Rent(4);
        try
        {
            await stream.ReadExactlyAsync(data.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
            return EndianUtilities.ToUInt32BigEndian(data);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(data);
        }
    }
}