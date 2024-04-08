//
// Copyright (c) 2008-2013, Kenneth Bell
// Copyright (c) 2017, Bianco Veigel
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
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using DiscUtils.Internal;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Vhdx;

internal sealed class LogEntry
{
    public const int LogSectorSize = 4 * Sizes.OneKiB;

    private readonly List<Descriptor> _descriptors = new List<Descriptor>();
    private readonly LogEntryHeader _header;

    private LogEntry(long position, LogEntryHeader header, List<Descriptor> descriptors)
    {
        Position = position;
        _header = header;
        _descriptors = descriptors;
    }

    public ulong FlushedFileOffset
    {
        get { return _header.FlushedFileOffset; }
    }

    public bool IsEmpty
    {
        get { return _descriptors.Count == 0; }
    }

    public ulong LastFileOffset
    {
        get { return _header.LastFileOffset; }
    }

    public Guid LogGuid
    {
        get { return _header.LogGuid; }
    }

    public IEnumerable<Range<ulong, ulong>> ModifiedExtents
    {
        get
        {
            foreach (var descriptor in _descriptors)
            {
                yield return new Range<ulong, ulong>(descriptor.FileOffset, descriptor.FileLength);
            }
        }
    }

    public long Position { get; }

    public ulong SequenceNumber
    {
        get { return _header.SequenceNumber; }
    }

    public uint Tail
    {
        get { return _header.Tail; }
    }

    public void Replay(Stream target)
    {
        if (IsEmpty)
        {
            return;
        }

        foreach (var descriptor in _descriptors)
        {
            descriptor.WriteData(target);
        }
    }

    public static bool TryRead(Stream logStream, out LogEntry entry)
    {
        var position = logStream.Position;

        var bytesToRead = LogEntryHeader.ByteCount;
        Span<byte> headerBuffer = stackalloc byte[bytesToRead];
        if (logStream.ReadMaximum(headerBuffer) != bytesToRead)
        {
            entry = null;
            return false;
        }

        var header = new LogEntryHeader();
        header.ReadFrom(headerBuffer);

        if (!header.IsValid)
        {
            entry = null;
            return false;
        }

        var entryLength = checked((int)header.EntryLength);
        var logEntryBuffer = ArrayPool<byte>.Shared.Rent(entryLength);

        try
        {
            headerBuffer.CopyTo(logEntryBuffer);

            bytesToRead = entryLength - LogEntryHeader.ByteCount;
            if (logStream.ReadMaximum(logEntryBuffer, LogEntryHeader.ByteCount, bytesToRead) != bytesToRead)
            {
                entry = null;
                return false;
            }

            Array.Clear(logEntryBuffer, 4, sizeof(uint));
            if (header.Checksum !=
                Crc32LittleEndian.Compute(Crc32Algorithm.Castagnoli, logEntryBuffer, 0, entryLength))
            {
                entry = null;
                return false;
            }

            var dataPos = MathUtilities.RoundUp((int)header.DescriptorCount * 32 + 64, LogSectorSize);

            var descriptors = new List<Descriptor>();
            for (var i = 0; i < header.DescriptorCount; ++i)
            {
                var offset = i * 32 + 64;
                Descriptor descriptor;

                var descriptorSig = EndianUtilities.ToUInt32LittleEndian(logEntryBuffer, offset);
                switch (descriptorSig)
                {
                    case Descriptor.ZeroDescriptorSignature:
                        descriptor = new ZeroDescriptor();
                        break;
                    case Descriptor.DataDescriptorSignature:
                        descriptor = new DataDescriptor(logEntryBuffer, dataPos);
                        dataPos += LogSectorSize;
                        break;
                    default:
                        entry = null;
                        return false;
                }

                descriptor.ReadFrom(logEntryBuffer, offset);
                if (!descriptor.IsValid(header.SequenceNumber))
                {
                    entry = null;
                    return false;
                }

                descriptors.Add(descriptor);
            }

            entry = new LogEntry(position, header, descriptors);
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(logEntryBuffer);
        }
    }

    private abstract class Descriptor : IByteArraySerializable
    {
        public const uint ZeroDescriptorSignature = 0x6F72657A;
        public const uint DataDescriptorSignature = 0x63736564;
        public const uint DataSectorSignature = 0x61746164;

        public ulong FileOffset;
        public ulong SequenceNumber;

        public abstract ulong FileLength { get; }

        public int Size
        {
            get { return 32; }
        }

        public abstract int ReadFrom(ReadOnlySpan<byte> buffer);

        public abstract void WriteTo(Span<byte> buffer);

        public abstract bool IsValid(ulong sequenceNumber);

        public abstract void WriteData(Stream target);
    }

    private sealed class ZeroDescriptor : Descriptor
    {
        public ulong ZeroLength;

        public override ulong FileLength
        {
            get { return ZeroLength; }
        }

        public override int ReadFrom(ReadOnlySpan<byte> buffer)
        {
            ZeroLength = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(8));
            FileOffset = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(16));
            SequenceNumber = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(24));

            return 32;
        }

        public override void WriteTo(Span<byte> buffer)
        {
            throw new NotImplementedException();
        }

        public override bool IsValid(ulong sequenceNumber)
        {
            return SequenceNumber == sequenceNumber;
        }

        public override void WriteData(Stream target)
        {
            target.Seek((long)FileOffset, SeekOrigin.Begin);
            const int size = (int)(4 * Sizes.OneKiB);
            var zeroBuffer = ArrayPool<byte>.Shared.Rent(size);
            try
            {
                Array.Clear(zeroBuffer, 0, size);
                var total = ZeroLength;
                while (total > 0)
                {
                    var count = size;
                    if (total < (uint)count)
                    {
                        count = (int)total;
                    }

                    target.Write(zeroBuffer, 0, count);
                    total -= (uint)count;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(zeroBuffer);
            }
        }
    }

    private sealed class DataDescriptor : Descriptor
    {
        private readonly byte[] _data;
        private readonly int _offset;
        public ulong LeadingBytes;
        public uint TrailingBytes;
        public uint DataSignature;

        public DataDescriptor(byte[] data, int offset)
        {
            _data = data;
            _offset = offset;
        }

        public override ulong FileLength
        {
            get { return LogSectorSize; }
        }

        public override int ReadFrom(ReadOnlySpan<byte> buffer)
        {
            TrailingBytes = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(4));
            LeadingBytes = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(8));
            FileOffset = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(16));
            SequenceNumber = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(24));

            DataSignature = EndianUtilities.ToUInt32LittleEndian(_data, _offset);

            return 32;
        }

        public override void WriteTo(Span<byte> buffer)
        {
            throw new NotImplementedException();
        }

        public override bool IsValid(ulong sequenceNumber)
        {
            return SequenceNumber == sequenceNumber
                   && _offset + LogSectorSize <= _data.Length
                   &&
                   EndianUtilities.ToUInt32LittleEndian(_data, _offset + LogSectorSize - 4) ==
                   (sequenceNumber & 0xFFFFFFFF)
                   && EndianUtilities.ToUInt32LittleEndian(_data, _offset + 4) == ((sequenceNumber >> 32) & 0xFFFFFFFF)
                   && DataSignature == DataSectorSignature;
        }

        public override void WriteData(Stream target)
        {
            target.Seek((long)FileOffset, SeekOrigin.Begin);
            Span<byte> leading = stackalloc byte[8];
            EndianUtilities.WriteBytesLittleEndian(LeadingBytes, leading);
            Span<byte> trailing = stackalloc byte[4];
            EndianUtilities.WriteBytesLittleEndian(TrailingBytes, trailing);

            target.Write(leading);
            target.Write(_data, _offset+8, 4084);
            target.Write(trailing);
        }
    }
}