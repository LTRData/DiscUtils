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
using System.Buffers;
using System.IO;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Fat;

internal class DirectoryEntry
{
    private readonly FatType _fatVariant;
    private readonly FatFileSystemOptions _options;
    private byte _attr;
    private ushort _creationDate;
    private ushort _creationTime;
    private byte _creationTimeTenth;
    private uint _fileSize;
    private ushort _firstClusterHi;
    private ushort _firstClusterLo;
    private ushort _lastAccessDate;
    private ushort _lastWriteDate;
    private ushort _lastWriteTime;

    internal DirectoryEntry(FatFileSystemOptions options, Stream stream, FatType fatVariant)
    {
        _options = options;
        _fatVariant = fatVariant;

        var bufferLength = 32;

        var buffer = ArrayPool<byte>.Shared.Rent(bufferLength);

        try
        {
            stream.ReadExactly(buffer, 0, bufferLength);

            // LFN entry
            if ((buffer[0] & 0xc0) == 0x40 && buffer[11] == 0x0f)
            {
                var lfn_entries = buffer[0] & 0x3F;

                bufferLength += 32 * lfn_entries;

                if (buffer.Length < bufferLength)
                {
                    var new_buffer = ArrayPool<byte>.Shared.Rent(bufferLength);
                    System.Buffer.BlockCopy(buffer, 0, new_buffer, 0, 32);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = new_buffer;
                }

                stream.ReadExactly(buffer, 32, 32 * lfn_entries);
            }

            Load(buffer, 0, bufferLength);
        }
        finally
        {
            if (buffer != null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    internal DirectoryEntry(FatFileSystemOptions options, FileName name, FatAttributes attrs, FatType fatVariant)
    {
        _options = options;
        _fatVariant = fatVariant;
        Name = name;
        _attr = (byte)attrs;
    }

    internal DirectoryEntry(DirectoryEntry toCopy)
    {
        _options = toCopy._options;
        _fatVariant = toCopy._fatVariant;
        Name = toCopy.Name;
        _attr = toCopy._attr;
        _creationTimeTenth = toCopy._creationTimeTenth;
        _creationTime = toCopy._creationTime;
        _creationDate = toCopy._creationDate;
        _lastAccessDate = toCopy._lastAccessDate;
        _firstClusterHi = toCopy._firstClusterHi;
        _lastWriteTime = toCopy._lastWriteTime;
        _firstClusterLo = toCopy._firstClusterLo;
        _fileSize = toCopy._fileSize;
    }

    public FatAttributes Attributes
    {
        get => (FatAttributes)_attr;
        set => _attr = (byte)value;
    }

    public DateTime CreationTime
    {
        get => FileTimeToDateTime(_creationDate, _creationTime, _creationTimeTenth);
        set => DateTimeToFileTime(value, out _creationDate, out _creationTime, out _creationTimeTenth);
    }

    public int FileSize
    {
        get => (int)_fileSize;
        set => _fileSize = (uint)value;
    }

    public uint FirstCluster
    {
        get
        {
            if (_fatVariant == FatType.Fat32)
            {
                return (uint)(_firstClusterHi << 16) | _firstClusterLo;
            }

            return _firstClusterLo;
        }

        set
        {
            if (_fatVariant == FatType.Fat32)
            {
                _firstClusterHi = (ushort)((value >> 16) & 0xFFFF);
            }

            _firstClusterLo = (ushort)(value & 0xFFFF);
        }
    }

    public DateTime LastAccessTime
    {
        get => FileTimeToDateTime(_lastAccessDate, 0, 0);
        set => DateTimeToFileTime(value, out _lastAccessDate);
    }

    public DateTime LastWriteTime
    {
        get => FileTimeToDateTime(_lastWriteDate, _lastWriteTime, 0);
        set => DateTimeToFileTime(value, out _lastWriteDate, out _lastWriteTime);
    }

    public FileName Name { get; set; }

    internal void WriteTo(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[32];

        Name.GetBytes(buffer);
        buffer[11] = _attr;
        buffer[13] = _creationTimeTenth;
        EndianUtilities.WriteBytesLittleEndian(_creationTime, buffer.Slice(14));
        EndianUtilities.WriteBytesLittleEndian(_creationDate, buffer.Slice(16));
        EndianUtilities.WriteBytesLittleEndian(_lastAccessDate, buffer.Slice(18));
        EndianUtilities.WriteBytesLittleEndian(_firstClusterHi, buffer.Slice(20));
        EndianUtilities.WriteBytesLittleEndian(_lastWriteTime, buffer.Slice(22));
        EndianUtilities.WriteBytesLittleEndian(_lastWriteDate, buffer.Slice(24));
        EndianUtilities.WriteBytesLittleEndian(_firstClusterLo, buffer.Slice(26));
        EndianUtilities.WriteBytesLittleEndian(_fileSize, buffer.Slice(28));

        stream.Write(buffer);
    }

    private static DateTime FileTimeToDateTime(ushort date, ushort time, byte tenths)
    {
        if (date is 0 or 0xFFFF)
        {
            // Return Epoch - this is an invalid date
            return FatFileSystem.Epoch;
        }

        var year = 1980 + ((date & 0xFE00) >> 9);
        var month = (date & 0x01E0) >> 5;
        var day = date & 0x001F;
        var hour = (time & 0xF800) >> 11;
        var minute = (time & 0x07E0) >> 5;
        var second = (time & 0x001F) * 2 + tenths / 100;
        var millis = tenths % 100 * 10;

        return new DateTime(year, month, day, hour, minute, second, millis);
    }

    private static void DateTimeToFileTime(DateTime value, out ushort date)
    {
        DateTimeToFileTime(value, out date, out var time, out var tenths);
    }

    private static void DateTimeToFileTime(DateTime value, out ushort date, out ushort time)
    {
        DateTimeToFileTime(value, out date, out time, out var tenths);
    }

    private static void DateTimeToFileTime(DateTime value, out ushort date, out ushort time, out byte tenths)
    {
        if (value.Year < 1980)
        {
            value = FatFileSystem.Epoch;
        }

        date =
            (ushort)((((value.Year - 1980) << 9) & 0xFE00) | ((value.Month << 5) & 0x01E0) | (value.Day & 0x001F));
        time =
            (ushort)(((value.Hour << 11) & 0xF800) | ((value.Minute << 5) & 0x07E0) | ((value.Second / 2) & 0x001F));
        tenths = (byte)(value.Second % 2 * 100 + value.Millisecond / 10);
    }

    private void Load(byte[] data, int offset, int count)
    {
        Name = new FileName(data.AsSpan(offset));

        offset += count - 32;

        _attr = data[offset + 11];
        _creationTimeTenth = data[offset + 13];
        _creationTime = EndianUtilities.ToUInt16LittleEndian(data, offset + 14);
        _creationDate = EndianUtilities.ToUInt16LittleEndian(data, offset + 16);
        _lastAccessDate = EndianUtilities.ToUInt16LittleEndian(data, offset + 18);
        _firstClusterHi = EndianUtilities.ToUInt16LittleEndian(data, offset + 20);
        _lastWriteTime = EndianUtilities.ToUInt16LittleEndian(data, offset + 22);
        _lastWriteDate = EndianUtilities.ToUInt16LittleEndian(data, offset + 24);
        _firstClusterLo = EndianUtilities.ToUInt16LittleEndian(data, offset + 26);
        _fileSize = EndianUtilities.ToUInt32LittleEndian(data, offset + 28);
    }
}