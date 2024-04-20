//
// Copyright (c) 2008-2012, Kenneth Bell
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
using System.Runtime.InteropServices;
using DiscUtils.Streams;
#if !NET5_0_OR_GREATER
using System.Security.Permissions;
#endif

namespace DiscUtils.Vhdx;

internal sealed class Metadata
{
    private readonly Stream _regionStream;

    public Guid Page83Data { get; }

    public Metadata(Stream regionStream)
    {
        _regionStream = regionStream;
        _regionStream.Position = 0;
        Table = _regionStream.ReadStruct<MetadataTable>();

        FileParameters = ReadStruct<FileParameters>(MetadataTable.FileParametersGuid, false);
        DiskSize = ReadValue(MetadataTable.VirtualDiskSizeGuid, false, EndianUtilities.ToUInt64LittleEndian);
        Page83Data = ReadValue(MetadataTable.Page83DataGuid, false, EndianUtilities.ToGuidLittleEndian);
        LogicalSectorSize = ReadValue(MetadataTable.LogicalSectorSizeGuid, false,
            EndianUtilities.ToUInt32LittleEndian);
        PhysicalSectorSize = ReadValue(MetadataTable.PhysicalSectorSizeGuid, false,
            EndianUtilities.ToUInt32LittleEndian);
        ParentLocator = ReadStruct<ParentLocator>(MetadataTable.ParentLocatorGuid, false);
    }

    private delegate T Reader<T>(ReadOnlySpan<byte> buffer);

    private delegate void Writer<T>(T val, byte[] buffer, int offset);

    public MetadataTable Table { get; }

    public FileParameters FileParameters { get; }

    public ulong DiskSize { get; }

    public uint LogicalSectorSize { get; }

    public uint PhysicalSectorSize { get; }

    public ParentLocator ParentLocator { get; }

    internal static Metadata Initialize(Stream metadataStream, FileParameters fileParameters, ulong diskSize,
                                        uint logicalSectorSize, uint physicalSectorSize, ParentLocator parentLocator)
    {
        var header = new MetadataTable();

        var dataOffset = (uint)(64 * Sizes.OneKiB);
        dataOffset += AddEntryStruct(fileParameters, MetadataTable.FileParametersGuid, MetadataEntryFlags.IsRequired,
            header, dataOffset, metadataStream);
        dataOffset += AddEntryValue(diskSize, EndianUtilities.WriteBytesLittleEndian, MetadataTable.VirtualDiskSizeGuid,
            MetadataEntryFlags.IsRequired | MetadataEntryFlags.IsVirtualDisk, header, dataOffset, metadataStream);
        dataOffset += AddEntryValue(Guid.NewGuid(), EndianUtilities.WriteBytesLittleEndian, MetadataTable.Page83DataGuid,
            MetadataEntryFlags.IsRequired | MetadataEntryFlags.IsVirtualDisk, header, dataOffset, metadataStream);
        dataOffset += AddEntryValue(logicalSectorSize, EndianUtilities.WriteBytesLittleEndian,
            MetadataTable.LogicalSectorSizeGuid, MetadataEntryFlags.IsRequired | MetadataEntryFlags.IsVirtualDisk,
            header, dataOffset, metadataStream);
        dataOffset += AddEntryValue(physicalSectorSize, EndianUtilities.WriteBytesLittleEndian,
            MetadataTable.PhysicalSectorSizeGuid, MetadataEntryFlags.IsRequired | MetadataEntryFlags.IsVirtualDisk,
            header, dataOffset, metadataStream);
        if (parentLocator != null)
        {
            dataOffset += AddEntryStruct(parentLocator, MetadataTable.ParentLocatorGuid,
                MetadataEntryFlags.IsRequired, header, dataOffset, metadataStream);
        }

        metadataStream.Position = 0;
        metadataStream.WriteStruct(header);
        return new Metadata(metadataStream);
    }

    private static uint AddEntryStruct<T>(T data, Guid id, MetadataEntryFlags flags, MetadataTable header,
                                          uint dataOffset, Stream stream)
        where T : IByteArraySerializable
    {
        var key = new MetadataEntryKey(id, (flags & MetadataEntryFlags.IsUser) != 0);
        var entry = new MetadataEntry
        {
            ItemId = id,
            Offset = dataOffset,
            Length = (uint)data.Size,
            Flags = flags
        };

        header.Entries[key] = entry;

        stream.Position = dataOffset;
        stream.WriteStruct(data);

        return entry.Length;
    }

#if !NET5_0_OR_GREATER
    [SecurityPermission(SecurityAction.Demand, UnmanagedCode = true)]
#endif
    private static uint AddEntryValue<T>(T data, Writer<T> writer, Guid id, MetadataEntryFlags flags,
                                         MetadataTable header, uint dataOffset, Stream stream)
    {
        var key = new MetadataEntryKey(id, (flags & MetadataEntryFlags.IsUser) != 0);
        var entry = new MetadataEntry
        {
            ItemId = id,
            Offset = dataOffset,
            Length = (uint)Marshal.SizeOf<T>(),
            Flags = flags
        };

        header.Entries[key] = entry;

        stream.Position = dataOffset;

        var buffer = new byte[entry.Length];
        writer(data, buffer, 0);
        stream.Write(buffer, 0, buffer.Length);

        return entry.Length;
    }

    private T ReadStruct<T>(Guid itemId, bool isUser)
        where T : IByteArraySerializable, new()
    {
        var key = new MetadataEntryKey(itemId, isUser);
        if (Table.Entries.TryGetValue(key, out var entry))
        {
            _regionStream.Position = entry.Offset;
            return _regionStream.ReadStruct<T>((int)entry.Length);
        }

        return default;
    }

#if !NETCOREAPP
    [SecurityPermission(SecurityAction.Demand, UnmanagedCode = true)]
#endif
    private T ReadValue<T>(Guid itemId, bool isUser, Reader<T> reader)
    {
        var key = new MetadataEntryKey(itemId, isUser);
        if (Table.Entries.TryGetValue(key, out var entry))
        {
            _regionStream.Position = entry.Offset;
            Span<byte> data = stackalloc byte[Marshal.SizeOf<T>()];
            _regionStream.ReadExactly(data);
            return reader(data);
        }

        return default;
    }
}