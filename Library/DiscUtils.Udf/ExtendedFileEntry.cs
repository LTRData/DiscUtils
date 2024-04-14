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
using System.Linq;
using DiscUtils.Streams;

namespace DiscUtils.Udf;

internal class ExtendedFileEntry : FileEntry, IByteArraySerializable
{
    public DateTime CreationTime;
    public ulong ObjectSize;
    public LongAllocationDescriptor StreamDirectoryIcb;

    public override int Size => throw new NotImplementedException();

    public override int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        DescriptorTag = EndianUtilities.ToStruct<DescriptorTag>(buffer);
        InformationControlBlock = EndianUtilities.ToStruct<InformationControlBlock>(buffer.Slice(16));
        Uid = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(36));
        Gid = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(40));
        Permissions = (FilePermissions)EndianUtilities.ToUInt32LittleEndian(buffer.Slice(44));
        FileLinkCount = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(48));
        RecordFormat = buffer[50];
        RecordDisplayAttributes = buffer[51];
        RecordLength = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(52));
        InformationLength = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(56));
        ObjectSize = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(64));
        LogicalBlocksRecorded = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(72));
        AccessTime = UdfUtilities.ParseTimestamp(buffer.Slice(80));
        ModificationTime = UdfUtilities.ParseTimestamp(buffer.Slice(92));
        CreationTime = UdfUtilities.ParseTimestamp(buffer.Slice(104));
        AttributeTime = UdfUtilities.ParseTimestamp(buffer.Slice(116));
        Checkpoint = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(128));
        ExtendedAttributeIcb = EndianUtilities.ToStruct<LongAllocationDescriptor>(buffer.Slice(136));
        StreamDirectoryIcb = EndianUtilities.ToStruct<LongAllocationDescriptor>(buffer.Slice(152));
        ImplementationIdentifier = EndianUtilities.ToStruct<ImplementationEntityIdentifier>(buffer.Slice(168));
        UniqueId = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(200));
        ExtendedAttributesLength = EndianUtilities.ToInt32LittleEndian(buffer.Slice(208));
        AllocationDescriptorsLength = EndianUtilities.ToInt32LittleEndian(buffer.Slice(212));
        AllocationDescriptors = EndianUtilities.ToByteArray(buffer.Slice(216 + ExtendedAttributesLength,
            AllocationDescriptorsLength));

        var eaData = EndianUtilities.ToByteArray(buffer.Slice(216, ExtendedAttributesLength));
        ExtendedAttributes = ReadExtendedAttributes(eaData).ToList();

        return 216 + ExtendedAttributesLength + AllocationDescriptorsLength;
    }

    public override void WriteTo(Span<byte> buffer)
    {
        throw new NotImplementedException();
    }
}