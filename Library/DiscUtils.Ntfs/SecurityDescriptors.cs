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
using System.Buffers;
using System.Diagnostics;
using System.IO;
using DiscUtils.Core.WindowsSecurity.AccessControl;
using DiscUtils.Internal;
using DiscUtils.Streams;

namespace DiscUtils.Ntfs;

internal sealed class SecurityDescriptors : IDiagnosticTraceable
{
    // File consists of pairs of duplicate blocks (one after the other), providing
    // redundancy.  When a pair is full, the next pair is used.
    private const int BlockSize = 0x40000;

    private readonly File _file;
    private readonly IndexView<HashIndexKey, HashIndexData> _hashIndex;
    private readonly IndexView<IdIndexKey, IdIndexData> _idIndex;
    private uint _nextId;
    private long _nextSpace;

    public SecurityDescriptors(File file)
    {
        _file = file;
        _hashIndex = new IndexView<HashIndexKey, HashIndexData>(file.GetIndex("$SDH"));
        _idIndex = new IndexView<IdIndexKey, IdIndexData>(file.GetIndex("$SII"));

        foreach (var entry in _idIndex.Entries)
        {
            if (entry.Key.Id > _nextId)
            {
                _nextId = entry.Key.Id;
            }

            var end = entry.Value.SdsOffset + entry.Value.SdsLength;
            if (end > _nextSpace)
            {
                _nextSpace = end;
            }
        }

        if (_nextId == 0)
        {
            _nextId = 256;
        }
        else
        {
            _nextId++;
        }

        _nextSpace = MathUtilities.RoundUp(_nextSpace, 16);
    }

    public void Dump(TextWriter writer, string indent)
    {
        writer.WriteLine($"{indent}SECURITY DESCRIPTORS");

        using var s = _file.OpenStream(AttributeType.Data, "$SDS", FileAccess.Read);

        byte[] allocated = null;

        var buffer = s.Length <= 1024
            ? stackalloc byte[(int)s.Length]
            : (allocated = ArrayPool<byte>.Shared.Rent((int)s.Length)).AsSpan(0, (int)s.Length);

        try
        {
            s.ReadExactly(buffer);

            foreach (var entry in _idIndex.Entries)
            {
                var pos = (int)entry.Value.SdsOffset;

                var rec = new SecurityDescriptorRecord();
                if (!rec.Read(buffer.Slice(pos)))
                {
                    break;
                }

                var secDescStr = "--unknown--";
                if (rec.SecurityDescriptor[0] != 0)
                {
                    var sd = new RawSecurityDescriptor(rec.SecurityDescriptor, 0);
                    secDescStr = sd.GetSddlForm(AccessControlSections.All);
                }

                writer.WriteLine($"{indent}  SECURITY DESCRIPTOR RECORD");
                writer.WriteLine($"{indent}           Hash: {rec.Hash}");
                writer.WriteLine($"{indent}             Id: {rec.Id}");
                writer.WriteLine($"{indent}    File Offset: {rec.OffsetInFile}");
                writer.WriteLine($"{indent}           Size: {rec.EntrySize}");
                writer.WriteLine($"{indent}          Value: {secDescStr}");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(allocated);
        }
    }

    public static SecurityDescriptors Initialize(File file)
    {
        file.CreateIndex("$SDH", 0, AttributeCollationRule.SecurityHash);
        file.CreateIndex("$SII", 0, AttributeCollationRule.UnsignedLong);
        file.CreateStream(AttributeType.Data, "$SDS");

        return new SecurityDescriptors(file);
    }

    public RawSecurityDescriptor GetDescriptorById(uint id)
    {
        if (_idIndex.TryGetValue(new IdIndexKey(id), out var data))
        {
            return ReadDescriptor(data).Descriptor;
        }

        return null;
    }

    private static bool SecurityDescriptorsEqual(SecurityDescriptor securityDescriptor, ReadOnlySpan<byte> bytes)
    {
        byte[] allocated = null;

        var storedByteForm = securityDescriptor.Size <= 1024
            ? stackalloc byte[securityDescriptor.Size]
            : (allocated = ArrayPool<byte>.Shared.Rent(securityDescriptor.Size)).AsSpan(0, securityDescriptor.Size);

        try
        {
            securityDescriptor.WriteTo(storedByteForm);

            if (bytes.SequenceEqual(storedByteForm))
            {
                return true;
            }

            return false;
        }
        finally
        {
            if (allocated is not null)
            {
                ArrayPool<byte>.Shared.Return(allocated);
            }
        }
    }

    public uint AddDescriptor(RawSecurityDescriptor newDescriptor)
    {
        // Search to see if this is a known descriptor
        var newDescObj = new SecurityDescriptor(newDescriptor);
        var newHash = newDescObj.CalcHash();

        byte[] allocated = null;

        var newByteForm = newDescObj.Size <= 1024
            ? stackalloc byte[newDescObj.Size]
            : (allocated = ArrayPool<byte>.Shared.Rent(newDescObj.Size)).AsSpan(0, newDescObj.Size);

        try
        {
            newDescObj.WriteTo(newByteForm);

            foreach (var entry in _hashIndex.FindAll(new HashFinder(newHash)))
            {
                var stored = ReadDescriptor(entry.Value);

                if (SecurityDescriptorsEqual(stored, newByteForm))
                {
                    return entry.Value.Id;
                }
            }

            var offset = _nextSpace;

            // Write the new descriptor to the end of the existing descriptors
            var record = new SecurityDescriptorRecord
            {
                SecurityDescriptor = newByteForm.ToArray(),
                Hash = newHash,
                Id = _nextId
            };

            // If we'd overflow into our duplicate block, skip over it to the
            // start of the next block
            if ((offset + record.Size) / BlockSize % 2 == 1)
            {
                _nextSpace = MathUtilities.RoundUp(offset, BlockSize * 2);
                offset = _nextSpace;
            }

            record.OffsetInFile = offset;

            byte[] allocated2 = null;

            var buffer = record.Size <= 1024
                ? stackalloc byte[record.Size]
                : (allocated2 = ArrayPool<byte>.Shared.Rent(record.Size)).AsSpan(0, record.Size);

            try
            {
                record.WriteTo(buffer);

                using (var s = _file.OpenStream(AttributeType.Data, "$SDS", FileAccess.ReadWrite))
                {
                    s.Position = _nextSpace;
                    s.Write(buffer);
                    s.Position = BlockSize + _nextSpace;
                    s.Write(buffer);
                }

                // Make the next descriptor land at the end of this one
                _nextSpace = MathUtilities.RoundUp(_nextSpace + buffer.Length, 16);
                _nextId++;
            }
            finally
            {
                if (allocated2 is not null)
                {
                    ArrayPool<byte>.Shared.Return(allocated2);
                }
            }

            // Update the indexes
            var hashIndexData = new HashIndexData
            {
                Hash = record.Hash,
                Id = record.Id,
                SdsOffset = record.OffsetInFile,
                SdsLength = (int)record.EntrySize
            };

            var hashIndexKey = new HashIndexKey
            {
                Hash = record.Hash,
                Id = record.Id
            };

            _hashIndex[hashIndexKey] = hashIndexData;

            var idIndexData = new IdIndexData
            {
                Hash = record.Hash,
                Id = record.Id,
                SdsOffset = record.OffsetInFile,
                SdsLength = (int)record.EntrySize
            };

            var idIndexKey = new IdIndexKey
            {
                Id = record.Id
            };

            _idIndex[idIndexKey] = idIndexData;

            _file.UpdateRecordInMft();

            return record.Id;
        }
        finally
        {
            if (allocated is not null)
            {
                ArrayPool<byte>.Shared.Return(allocated);
            }
        }
    }

    private SecurityDescriptor ReadDescriptor(IndexData data)
    {
        using var s = _file.OpenStream(AttributeType.Data, "$SDS", FileAccess.Read);

        s.Position = data.SdsOffset;

        var record = s.ReadStruct<SecurityDescriptorRecord>(data.SdsLength);

        return new SecurityDescriptor(new RawSecurityDescriptor(record.SecurityDescriptor, 0));
    }

    internal abstract class IndexData
    {
        public uint Hash;
        public uint Id;
        public int SdsLength;
        public long SdsOffset;

        public override string ToString() =>
            $"[Data-Hash:{Hash},Id:{Id},SdsOffset:{SdsOffset},SdsLength:{SdsLength}]";
    }

    internal struct HashIndexKey : IByteArraySerializable
    {
        public uint Hash;
        public uint Id;

        public int Size
        {
            get { return 8; }
        }

        public int ReadFrom(ReadOnlySpan<byte> buffer)
        {
            Hash = EndianUtilities.ToUInt32LittleEndian(buffer);
            Id = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(4));
            return 8;
        }

        public void WriteTo(Span<byte> buffer)
        {
            EndianUtilities.WriteBytesLittleEndian(Hash, buffer);
            EndianUtilities.WriteBytesLittleEndian(Id, buffer.Slice(4));
        }

        public override string ToString() =>
            $"[Key-Hash:{Hash},Id:{Id}]";
    }

    internal sealed class HashIndexData : IndexData, IByteArraySerializable
    {
        public int Size
        {
            get { return 0x14; }
        }

        public int ReadFrom(ReadOnlySpan<byte> buffer)
        {
            Hash = EndianUtilities.ToUInt32LittleEndian(buffer);
            Id = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x04));
            SdsOffset = EndianUtilities.ToInt64LittleEndian(buffer.Slice(0x08));
            SdsLength = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x10));
            return 0x14;
        }

        public void WriteTo(Span<byte> buffer)
        {
            EndianUtilities.WriteBytesLittleEndian(Hash, buffer);
            EndianUtilities.WriteBytesLittleEndian(Id, buffer.Slice(0x04));
            EndianUtilities.WriteBytesLittleEndian(SdsOffset, buffer.Slice(0x08));
            EndianUtilities.WriteBytesLittleEndian(SdsLength, buffer.Slice(0x10));
            ////Array.Copy(new byte[] { (byte)'I', 0, (byte)'I', 0 }, 0, buffer.Slice(0x14, 4));
        }
    }

    internal struct IdIndexKey : IByteArraySerializable
    {
        public uint Id;

        public IdIndexKey(uint id)
        {
            Id = id;
        }

        public int Size
        {
            get { return 4; }
        }

        public int ReadFrom(ReadOnlySpan<byte> buffer)
        {
            Id = EndianUtilities.ToUInt32LittleEndian(buffer);
            return 4;
        }

        public void WriteTo(Span<byte> buffer)
        {
            EndianUtilities.WriteBytesLittleEndian(Id, buffer);
        }

        public override string ToString() => $"[Key-Id:{Id}]";
    }

    internal sealed class IdIndexData : IndexData, IByteArraySerializable
    {
        public int Size
        {
            get { return 0x14; }
        }

        public int ReadFrom(ReadOnlySpan<byte> buffer)
        {
            Hash = EndianUtilities.ToUInt32LittleEndian(buffer);
            Id = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x04));
            SdsOffset = EndianUtilities.ToInt64LittleEndian(buffer.Slice(0x08));
            SdsLength = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x10));
            return 0x14;
        }

        public void WriteTo(Span<byte> buffer)
        {
            EndianUtilities.WriteBytesLittleEndian(Hash, buffer);
            EndianUtilities.WriteBytesLittleEndian(Id, buffer.Slice(0x04));
            EndianUtilities.WriteBytesLittleEndian(SdsOffset, buffer.Slice(0x08));
            EndianUtilities.WriteBytesLittleEndian(SdsLength, buffer.Slice(0x10));
        }
    }

    private class HashFinder : IComparable<HashIndexKey>
    {
        private readonly uint _toMatch;

        public HashFinder(uint toMatch)
        {
            _toMatch = toMatch;
        }

        public int CompareTo(HashIndexKey other)
        {
            return CompareTo(other.Hash);
        }

        public int CompareTo(uint otherHash)
        {
            if (_toMatch < otherHash)
            {
                return -1;
            }

            if (_toMatch > otherHash)
            {
                return 1;
            }

            return 0;
        }
    }
}