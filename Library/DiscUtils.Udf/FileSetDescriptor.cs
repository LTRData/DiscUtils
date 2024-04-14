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
using DiscUtils.Streams;

namespace DiscUtils.Udf;

internal class FileSetDescriptor : IByteArraySerializable
{
    public string AbstractFileIdentifier;
    public uint CharacterSetList;
    public string CopyrightFileIdentifier;
    public DescriptorTag DescriptorTag;
    public DomainEntityIdentifier DomainIdentifier;
    public CharacterSetSpecification FileSetCharset;
    public uint FileSetDescriptorNumber;
    public string FileSetIdentifier;
    public uint FileSetNumber;
    public ushort InterchangeLevel;
    public string LogicalVolumeIdentifier;
    public CharacterSetSpecification LogicalVolumeIdentifierCharset;
    public uint MaximumCharacterSetList;
    public ushort MaximumInterchangeLevel;
    public LongAllocationDescriptor NextExtent;
    public DateTime RecordingTime;
    public LongAllocationDescriptor RootDirectoryIcb;
    public LongAllocationDescriptor SystemStreamDirectoryIcb;

    public int Size => 512;

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        DescriptorTag = EndianUtilities.ToStruct<DescriptorTag>(buffer);
        RecordingTime = UdfUtilities.ParseTimestamp(buffer.Slice(16));
        InterchangeLevel = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(28));
        MaximumInterchangeLevel = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(30));
        CharacterSetList = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(32));
        MaximumCharacterSetList = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(36));
        FileSetNumber = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(40));
        FileSetDescriptorNumber = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(44));
        LogicalVolumeIdentifierCharset = EndianUtilities.ToStruct<CharacterSetSpecification>(buffer.Slice(48));
        LogicalVolumeIdentifier = UdfUtilities.ReadDString(buffer.Slice(112, 128));
        FileSetCharset = EndianUtilities.ToStruct<CharacterSetSpecification>(buffer.Slice(240));
        FileSetIdentifier = UdfUtilities.ReadDString(buffer.Slice(304, 32));
        CopyrightFileIdentifier = UdfUtilities.ReadDString(buffer.Slice(336, 32));
        AbstractFileIdentifier = UdfUtilities.ReadDString(buffer.Slice(368, 32));
        RootDirectoryIcb = EndianUtilities.ToStruct<LongAllocationDescriptor>(buffer.Slice(400));
        DomainIdentifier = EndianUtilities.ToStruct<DomainEntityIdentifier>(buffer.Slice(416));
        NextExtent = EndianUtilities.ToStruct<LongAllocationDescriptor>(buffer.Slice(448));
        SystemStreamDirectoryIcb = EndianUtilities.ToStruct<LongAllocationDescriptor>(buffer.Slice(464));

        return 512;
    }

    void IByteArraySerializable.WriteTo(Span<byte> buffer)
    {
        throw new NotImplementedException();
    }
}