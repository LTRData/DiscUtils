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

namespace DiscUtils.Lvm;

internal class PhysicalVolumeLabel : IByteArraySerializable
{
    public const string LABEL_ID = "LABELONE";
    public const string LVM2_LABEL = "LVM2 001";

    public string Label;
    public ulong Sector;
    public ulong Crc;
    public ulong CalculatedCrc;
    public ulong Offset;
    public string Label2;

    /// <inheritdoc />
    public int Size => PhysicalVolume.SECTOR_SIZE;

    /// <inheritdoc />
    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        var latin1Encoding = EncodingUtilities.GetLatin1Encoding();

        Label = latin1Encoding.GetString(buffer.Slice(0, 0x8));
        Sector = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(0x8));
        Crc = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x10));
        CalculatedCrc = PhysicalVolume.CalcCrc(buffer.Slice(0x14, PhysicalVolume.SECTOR_SIZE - 0x14));
        Offset = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x14));
        Label2 = latin1Encoding.GetString(buffer.Slice(0x18, 0x8));

        return Size;
    }

    /// <inheritdoc />
    void IByteArraySerializable.WriteTo(Span<byte> buffer)
    {
        throw new NotImplementedException();
    }
}
