//
// Copyright (c) 2016, Bianco Veigel
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
using System.Collections.Generic;
using System.Text;

namespace DiscUtils.Lvm;

internal class PvHeader : IByteArraySerializable
{
    public string Uuid;
    public ulong DeviceSize;
    public List<DiskArea> DiskAreas;
    public List<DiskArea> MetadataDiskAreas;
    /// <inheritdoc />
    public int Size => PhysicalVolume.SECTOR_SIZE;

    /// <inheritdoc />
    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        Uuid = ReadUuid(buffer);
        DeviceSize = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(0x20));
        var areas = new List<DiskArea>();
        var areaOffset = 0x28;
        while (true)
        {
            var area = new DiskArea();
            areaOffset += area.ReadFrom(buffer.Slice(areaOffset));
            if (area.Offset == 0 && area.Length == 0)
            {
                break;
            }

            areas.Add(area);
        }

        DiskAreas = areas;
        areas = [];
        while (true)
        {
            var area = new DiskArea();
            areaOffset += area.ReadFrom(buffer.Slice(areaOffset));
            if (area.Offset == 0 && area.Length == 0)
            {
                break;
            }

            areas.Add(area);
        }

        MetadataDiskAreas = areas;
        return Size;
    }

    /// <inheritdoc />
    void IByteArraySerializable.WriteTo(Span<byte> buffer)
    {
        throw new NotImplementedException();
    }

    private static string ReadUuid(ReadOnlySpan<byte> buffer)
    {
        var latin1Encoding = EncodingUtilities.GetLatin1Encoding();

        return new StringBuilder()
            .Append(latin1Encoding.GetString(buffer.Slice(0, 0x6))).Append('-')
            .Append(latin1Encoding.GetString(buffer.Slice(0x6, 0x4))).Append('-')
            .Append(latin1Encoding.GetString(buffer.Slice(0xA, 0x4))).Append('-')
            .Append(latin1Encoding.GetString(buffer.Slice(0xE, 0x4))).Append('-')
            .Append(latin1Encoding.GetString(buffer.Slice(0x12, 0x4))).Append('-')
            .Append(latin1Encoding.GetString(buffer.Slice(0x16, 0x4))).Append('-')
            .Append(latin1Encoding.GetString(buffer.Slice(0x1A, 0x6)))
            .ToString();
    }
}
