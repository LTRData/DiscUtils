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
using System.IO;
using DiscUtils.Streams;
using DiscUtils.Vfs;

namespace DiscUtils.Udf;

internal class FileIdentifier : VfsDirEntry, IByteArraySerializable
{
    public DescriptorTag DescriptorTag;
    public FileCharacteristic FileCharacteristics;
    public LongAllocationDescriptor FileLocation;
    public ushort FileVersionNumber;
    public byte[] ImplementationUse;
    public ushort ImplementationUseLength;
    public string Name;
    public byte NameLength;

    public override DateTime CreationTimeUtc => throw new NotSupportedException();

    public override FileAttributes FileAttributes => throw new NotSupportedException();

    public override string FileName => Name;

    public override bool HasVfsFileAttributes => false;

    public override bool HasVfsTimeInfo => false;

    public override bool IsDirectory => (FileCharacteristics & FileCharacteristic.Directory) != 0;

    public override bool IsSymlink => false;

    public override DateTime LastAccessTimeUtc => throw new NotSupportedException();

    public override DateTime LastWriteTimeUtc => throw new NotSupportedException();

    public override long UniqueCacheId => (long)FileLocation.ExtentLocation.Partition << 32 | FileLocation.ExtentLocation.LogicalBlock;

    public int Size => throw new NotImplementedException();

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        DescriptorTag = EndianUtilities.ToStruct<DescriptorTag>(buffer);
        FileVersionNumber = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(16));
        FileCharacteristics = (FileCharacteristic)buffer[18];
        NameLength = buffer[19];
        FileLocation = EndianUtilities.ToStruct<LongAllocationDescriptor>(buffer.Slice(20));
        ImplementationUseLength = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(36));
        ImplementationUse = EndianUtilities.ToByteArray(buffer.Slice(38, ImplementationUseLength));
        Name = UdfUtilities.ReadDCharacters(buffer.Slice(38 + ImplementationUseLength, NameLength));

        return MathUtilities.RoundUp(38 + ImplementationUseLength + NameLength, 4);
    }

    void IByteArraySerializable.WriteTo(Span<byte> buffer)
    {
        throw new NotImplementedException();
    }
}