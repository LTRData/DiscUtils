﻿//
// Copyright (c) 2014, Quamotion
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
using DiscUtils.Streams;

namespace DiscUtils.HfsPlus;

internal class CompressionAttribute
{
    //private byte _attrData1;
    //private byte _attrData2;
    private uint _compressionMagic;
    //private uint _recordType;
    //private uint _reserved1;
    //private uint _reserved2;
    //private uint _reserved3;

    public uint AttrSize { get; private set; }

    public string CompressionMagic => Encoding.ASCII.GetString(BitConverter.GetBytes(_compressionMagic));

    public FileCompressionType CompressionType { get; private set; }

    public static int Size => 32;

    public uint UncompressedSize { get; private set; }

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        //_recordType = EndianUtilities.ToUInt32BigEndian(buffer);
        //_reserved1 = EndianUtilities.ToUInt32BigEndian(buffer.Slice(4));
        //_reserved1 = EndianUtilities.ToUInt32BigEndian(buffer.Slice(8));
        AttrSize = EndianUtilities.ToUInt32BigEndian(buffer.Slice(12));
        _compressionMagic = EndianUtilities.ToUInt32BigEndian(buffer.Slice(16));
        CompressionType = (FileCompressionType)EndianUtilities.ToUInt32LittleEndian(buffer.Slice(20));
        UncompressedSize = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(24));
        //_reserved3 = EndianUtilities.ToUInt32BigEndian(buffer.Slice(28));

        return Size;
    }
}