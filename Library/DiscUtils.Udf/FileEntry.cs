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
using System.Collections.Generic;
using System.Linq;
using DiscUtils.Streams;

namespace DiscUtils.Udf;

internal class FileEntry : IByteArraySerializable
{
    public DateTime AccessTime;
    public byte[] AllocationDescriptors;
    public int AllocationDescriptorsLength;
    public DateTime AttributeTime;
    public uint Checkpoint;
    public DescriptorTag DescriptorTag;
    public LongAllocationDescriptor ExtendedAttributeIcb;
    public List<ExtendedAttributeRecord> ExtendedAttributes;
    public int ExtendedAttributesLength;
    public ushort FileLinkCount;
    public uint Gid;
    public ImplementationEntityIdentifier ImplementationIdentifier;
    public InformationControlBlock InformationControlBlock;
    public ulong InformationLength;
    public ulong LogicalBlocksRecorded;
    public DateTime ModificationTime;
    public FilePermissions Permissions;
    public byte RecordDisplayAttributes;
    public byte RecordFormat;
    public uint RecordLength;
    public uint Uid;
    public ulong UniqueId;

    public virtual int Size => throw new NotImplementedException();

    public virtual int ReadFrom(ReadOnlySpan<byte> buffer)
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
        LogicalBlocksRecorded = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(64));
        AccessTime = UdfUtilities.ParseTimestamp(buffer.Slice(72));
        ModificationTime = UdfUtilities.ParseTimestamp(buffer.Slice(84));
        AttributeTime = UdfUtilities.ParseTimestamp(buffer.Slice(96));
        Checkpoint = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(108));
        ExtendedAttributeIcb = EndianUtilities.ToStruct<LongAllocationDescriptor>(buffer.Slice(112));
        ImplementationIdentifier = EndianUtilities.ToStruct<ImplementationEntityIdentifier>(buffer.Slice(128));
        UniqueId = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(160));
        ExtendedAttributesLength = EndianUtilities.ToInt32LittleEndian(buffer.Slice(168));
        AllocationDescriptorsLength = EndianUtilities.ToInt32LittleEndian(buffer.Slice(172));
        AllocationDescriptors = EndianUtilities.ToByteArray(buffer.Slice(176 + ExtendedAttributesLength,
            AllocationDescriptorsLength));

        var eaData = EndianUtilities.ToByteArray(buffer.Slice(176, ExtendedAttributesLength));
        ExtendedAttributes = ReadExtendedAttributes(eaData).ToList();

        return 176 + ExtendedAttributesLength + AllocationDescriptorsLength;
    }

    public virtual void WriteTo(Span<byte> buffer)
    {
        throw new NotImplementedException();
    }

    protected static IEnumerable<ExtendedAttributeRecord> ReadExtendedAttributes(ReadOnlyMemory<byte> eaData)
    {
        if (!eaData.IsEmpty)
        {
            var eaTag = new DescriptorTag();
            eaTag.ReadFrom(eaData.Span);

            var implAttrLocation = EndianUtilities.ToInt32LittleEndian(eaData.Span.Slice(16));
            //var appAttrLocation = EndianUtilities.ToInt32LittleEndian(eaData.Span.Slice(20));

            var pos = 24;
            while (pos < eaData.Length)
            {
                ExtendedAttributeRecord ea;

                if (pos >= implAttrLocation)
                {
                    ea = new ImplementationUseExtendedAttributeRecord();
                }
                else
                {
                    ea = new ExtendedAttributeRecord();
                }

                var numRead = ea.ReadFrom(eaData.Span.Slice(pos));
                yield return ea;

                pos += numRead;
            }
        }
    }
}