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
using System.Text;

namespace DiscUtils.Iso9660;

internal class DirectoryRecord
{
    public uint DataLength;
    public byte ExtendedAttributeRecordLength;
    public string FileIdentifier;
    public byte FileUnitSize;
    public FileFlags Flags;
    public byte InterleaveGapSize;
    public uint LocationOfExtent;
    public DateTime RecordingDateAndTime;
    public byte[] SystemUseData;
    public ushort VolumeSequenceNumber;

    public static int ReadFrom(ReadOnlySpan<byte> src, Encoding enc, out DirectoryRecord record)
    {
        int length = src[0];

        record = new DirectoryRecord
        {
            ExtendedAttributeRecordLength = src[1],
            LocationOfExtent = IsoUtilities.ToUInt32FromBoth(src.Slice(2)),
            DataLength = IsoUtilities.ToUInt32FromBoth(src.Slice(10)),
            RecordingDateAndTime = IsoUtilities.ToUTCDateTimeFromDirectoryTime(src.Slice(18)),
            Flags = (FileFlags)src[25],
            FileUnitSize = src[26],
            InterleaveGapSize = src[27],
            VolumeSequenceNumber = IsoUtilities.ToUInt16FromBoth(src.Slice(28))
        };
        var lengthOfFileIdentifier = src[32];
        record.FileIdentifier = IsoUtilities.ReadChars(src.Slice(33, lengthOfFileIdentifier), enc);

        var padding = (lengthOfFileIdentifier & 1) == 0 ? 1 : 0;
        var startSystemArea = lengthOfFileIdentifier + padding + 33;
        var lenSystemArea = length - startSystemArea;
        if (lenSystemArea > 0)
        {
            record.SystemUseData = src.Slice(startSystemArea, lenSystemArea).ToArray();
        }

        return length;
    }

    public static uint CalcLength(string name, Encoding enc)
    {
        int nameBytes;
        if (name.Length == 1 && name[0] <= 1)
        {
            nameBytes = 1;
        }
        else
        {
            nameBytes = enc.GetByteCount(name);
        }

        return (uint)(33 + nameBytes + ((nameBytes & 0x1) == 0 ? 1 : 0));
    }

    internal int WriteTo(Span<byte> buffer, Encoding enc)
    {
        var length = CalcLength(FileIdentifier, enc);
        buffer[0] = (byte)length;
        buffer[1] = ExtendedAttributeRecordLength;
        IsoUtilities.ToBothFromUInt32(buffer.Slice(2), LocationOfExtent);
        IsoUtilities.ToBothFromUInt32(buffer.Slice(10), DataLength);
        IsoUtilities.ToDirectoryTimeFromUTC(buffer.Slice(18), RecordingDateAndTime);
        buffer[25] = (byte)Flags;
        buffer[26] = FileUnitSize;
        buffer[27] = InterleaveGapSize;
        IsoUtilities.ToBothFromUInt16(buffer.Slice(28), VolumeSequenceNumber);
        byte lengthOfFileIdentifier;

        if (FileIdentifier.Length == 1 && FileIdentifier[0] <= 1)
        {
            buffer[33] = (byte)FileIdentifier[0];
            lengthOfFileIdentifier = 1;
        }
        else
        {
            lengthOfFileIdentifier =
                (byte)
                IsoUtilities.WriteString(buffer.Slice(33, (int)(length - 33)), pad: false, FileIdentifier.AsSpan(), enc);
        }

        buffer[32] = lengthOfFileIdentifier;
        return (int)length;
    }

    public override string ToString() => FileIdentifier;
}