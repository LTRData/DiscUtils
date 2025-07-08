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
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;
using System.Buffers;


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

    private delegate void Writer<T>(T val, Span<byte> buffer);

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
        dataOffset += AddEntryValue(logicalSectorSize, EndianUtilities.WriteBytesLittleEndian,
            MetadataTable.LogicalSectorSizeGuid, MetadataEntryFlags.IsRequired | MetadataEntryFlags.IsVirtualDisk,
            header, dataOffset, metadataStream);
        dataOffset += AddEntryValue(physicalSectorSize, EndianUtilities.WriteBytesLittleEndian,
            MetadataTable.PhysicalSectorSizeGuid, MetadataEntryFlags.IsRequired | MetadataEntryFlags.IsVirtualDisk,
            header, dataOffset, metadataStream);
        dataOffset += AddEntryValue(Guid.NewGuid(), EndianUtilities.WriteBytesLittleEndian, MetadataTable.Page83DataGuid,
            MetadataEntryFlags.IsRequired | MetadataEntryFlags.IsVirtualDisk, header, dataOffset, metadataStream);
        if (parentLocator != null)
        {
            dataOffset += AddEntryStruct(parentLocator, MetadataTable.ParentLocatorGuid,
                MetadataEntryFlags.IsRequired, header, dataOffset, metadataStream);
        }

        metadataStream.Position = 0;
        metadataStream.WriteStruct(header);
        return new Metadata(metadataStream);
    }

    internal static async ValueTask<Metadata> InitializeAsync(Stream metadataStream, FileParameters fileParameters, ulong diskSize,
                                        uint logicalSectorSize, uint physicalSectorSize, ParentLocator parentLocator, CancellationToken cancellationToken)
    {
        var header = new MetadataTable();

        var dataOffset = (uint)(64 * Sizes.OneKiB);
        dataOffset += await AddEntryStructAsync(fileParameters, MetadataTable.FileParametersGuid, MetadataEntryFlags.IsRequired,
            header, dataOffset, metadataStream, cancellationToken).ConfigureAwait(false);
        dataOffset += await AddEntryValueAsync(diskSize, EndianUtilities.WriteBytesLittleEndian, MetadataTable.VirtualDiskSizeGuid,
            MetadataEntryFlags.IsRequired | MetadataEntryFlags.IsVirtualDisk, header, dataOffset, metadataStream, cancellationToken).ConfigureAwait(false);
        dataOffset += await AddEntryValueAsync(logicalSectorSize, EndianUtilities.WriteBytesLittleEndian,
            MetadataTable.LogicalSectorSizeGuid, MetadataEntryFlags.IsRequired | MetadataEntryFlags.IsVirtualDisk,
            header, dataOffset, metadataStream, cancellationToken).ConfigureAwait(false);
        dataOffset += await AddEntryValueAsync(physicalSectorSize, EndianUtilities.WriteBytesLittleEndian,
            MetadataTable.PhysicalSectorSizeGuid, MetadataEntryFlags.IsRequired | MetadataEntryFlags.IsVirtualDisk,
            header, dataOffset, metadataStream, cancellationToken).ConfigureAwait(false);
        dataOffset += await AddEntryValueAsync(Guid.NewGuid(), EndianUtilities.WriteBytesLittleEndian, MetadataTable.Page83DataGuid,
            MetadataEntryFlags.IsRequired | MetadataEntryFlags.IsVirtualDisk, header, dataOffset, metadataStream, cancellationToken).ConfigureAwait(false);
        if (parentLocator != null)
        {
            dataOffset += await AddEntryStructAsync(parentLocator, MetadataTable.ParentLocatorGuid,
                MetadataEntryFlags.IsRequired, header, dataOffset, metadataStream, cancellationToken).ConfigureAwait(false);
        }

        metadataStream.Position = 0;
        await metadataStream.WriteStructAsync(header, cancellationToken).ConfigureAwait(false);
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

    private static async ValueTask<uint> AddEntryStructAsync<T>(T data, Guid id, MetadataEntryFlags flags, MetadataTable header,
                                          uint dataOffset, Stream stream, CancellationToken cancellationToken)
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
        await stream.WriteStructAsync(data, cancellationToken).ConfigureAwait(false);

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

        Span<byte> buffer = stackalloc byte[(int)entry.Length];
        writer(data, buffer);
        stream.Write(buffer);

        return entry.Length;
    }

#if !NET5_0_OR_GREATER
    [SecurityPermission(SecurityAction.Demand, UnmanagedCode = true)]
#endif
    private static async ValueTask <uint> AddEntryValueAsync<T>(T data, Writer<T> writer, Guid id, MetadataEntryFlags flags,
                                         MetadataTable header, uint dataOffset, Stream stream, CancellationToken cancellationToken)
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

        var buffer = ArrayPool<byte>.Shared.Rent((int)entry.Length);
        try
        {
            writer(data, buffer);
            await stream.WriteAsync(buffer.AsMemory(0, (int)entry.Length), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

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