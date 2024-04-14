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

using System.Collections.Generic;
using System.IO;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Ntfs;

internal class NtfsAttribute : IDiagnosticTraceable
{
    private Buffer _cachedRawBuffer;
    protected FileRecordReference _containingFile;
    protected Dictionary<AttributeReference, AttributeRecord> _extents;
    protected File _file;
    protected AttributeRecord _primaryRecord;

    protected NtfsAttribute(File file, FileRecordReference containingFile, AttributeRecord record)
    {
        _file = file;
        _containingFile = containingFile;
        _primaryRecord = record;
        _extents = new Dictionary<AttributeReference, AttributeRecord>
        {
            { new AttributeReference(containingFile, record.AttributeId), _primaryRecord }
        };
    }

    protected string AttributeTypeName => _primaryRecord.AttributeType switch
    {
        AttributeType.StandardInformation => "STANDARD INFORMATION",
        AttributeType.FileName => "FILE NAME",
        AttributeType.SecurityDescriptor => "SECURITY DESCRIPTOR",
        AttributeType.Data => "DATA",
        AttributeType.Bitmap => "BITMAP",
        AttributeType.VolumeName => "VOLUME NAME",
        AttributeType.VolumeInformation => "VOLUME INFORMATION",
        AttributeType.IndexRoot => "INDEX ROOT",
        AttributeType.IndexAllocation => "INDEX ALLOCATION",
        AttributeType.ObjectId => "OBJECT ID",
        AttributeType.ReparsePoint => "REPARSE POINT",
        AttributeType.AttributeList => "ATTRIBUTE LIST",
        _ => "UNKNOWN",
    };

    public long CompressedDataSize
    {
        get
        {
            if (FirstExtent is not NonResidentAttributeRecord firstExtent)
            {
                return FirstExtent.AllocatedLength;
            }

            return firstExtent.CompressedDataSize;
        }

        set
        {
            if (FirstExtent is NonResidentAttributeRecord firstExtent)
            {
                firstExtent.CompressedDataSize = value;
            }
        }
    }

    public int CompressionUnitSize
    {
        get
        {
            if (FirstExtent is not NonResidentAttributeRecord firstExtent)
            {
                return 0;
            }

            return firstExtent.CompressionUnitSize;
        }

        set
        {
            if (FirstExtent is NonResidentAttributeRecord firstExtent)
            {
                firstExtent.CompressionUnitSize = value;
            }
        }
    }

    public IDictionary<AttributeReference, AttributeRecord> Extents => _extents;

    public AttributeRecord FirstExtent
    {
        get
        {
            if (_extents != null)
            {
                foreach (var extent in _extents)
                {
                    if (extent.Value is not NonResidentAttributeRecord nonResident)
                    {
                        // Resident attribute, so there can only be one...
                        return extent.Value;
                    }

                    if (nonResident.StartVcn == 0)
                    {
                        return extent.Value;
                    }
                }
            }

            throw new InvalidDataException("Attribute with no initial extent");
        }
    }

    public AttributeFlags Flags
    {
        get => _primaryRecord.Flags;

        set
        {
            _primaryRecord.Flags = value;
            _cachedRawBuffer = null;
        }
    }

    public ushort Id => _primaryRecord.AttributeId;

    public bool IsNonResident => _primaryRecord.IsNonResident;

    public AttributeRecord LastExtent
    {
        get
        {
            AttributeRecord last = null;

            if (_extents != null)
            {
                long lastVcn = 0;
                foreach (var extent in _extents)
                {
                    if (extent.Value is not NonResidentAttributeRecord nonResident)
                    {
                        // Resident attribute, so there can only be one...
                        return extent.Value;
                    }

                    if (nonResident.LastVcn >= lastVcn)
                    {
                        last = extent.Value;
                        lastVcn = nonResident.LastVcn;
                    }
                }
            }

            return last;
        }
    }

    public long Length => _primaryRecord.DataLength;

    public string Name => _primaryRecord.Name;

    public AttributeRecord PrimaryRecord => _primaryRecord;

    public Buffer RawBuffer
    {
        get
        {
            if (_cachedRawBuffer == null)
            {
                if (_primaryRecord.IsNonResident)
                {
                    _cachedRawBuffer = new NonResidentAttributeBuffer(_file, this);
                }
                else
                {
                    _cachedRawBuffer = ((ResidentAttributeRecord)_primaryRecord).DataBuffer;
                }
            }

            return _cachedRawBuffer;
        }
    }

    public List<AttributeRecord> Records
    {
        get
        {
            var records = new List<AttributeRecord>(_extents.Values);
            records.Sort(AttributeRecord.CompareStartVcns);
            return records;
        }
    }

    public AttributeReference Reference => new AttributeReference(_containingFile, _primaryRecord.AttributeId);

    public AttributeType Type => _primaryRecord.AttributeType;

    public virtual void Dump(TextWriter writer, string indent)
    {
        writer.WriteLine($"{indent}{AttributeTypeName} ATTRIBUTE ({(Name == null ? "No Name" : Name)})");

        writer.WriteLine($"{indent}  Length: {_primaryRecord.DataLength} bytes");
        if (_primaryRecord.DataLength == 0)
        {
            writer.WriteLine($"{indent}    Data: <none>");
        }
        else
        {
            try
            {
                using var s = Open(FileAccess.Read);
                var hex = string.Empty;
                System.Span<byte> buffer = stackalloc byte[32];
                var numBytes = s.Read(buffer);
                for (var i = 0; i < numBytes; ++i)
                {
                    hex = hex + $" {buffer[i]:X2}";
                }

                writer.WriteLine($"{indent}    Data: {hex}{(numBytes < s.Length ? "..." : string.Empty)}");
            }
            catch
            {
                writer.WriteLine($"{indent}    Data: <can't read>");
            }
        }

        _primaryRecord.Dump(writer, $"{indent}  ");
    }

    public static NtfsAttribute FromRecord(File file, FileRecordReference recordFile, AttributeRecord record)
    {
        return record.AttributeType switch
        {
            AttributeType.StandardInformation => new StructuredNtfsAttribute<StandardInformation>(file, recordFile, record),
            AttributeType.FileName => new StructuredNtfsAttribute<FileNameRecord>(file, recordFile, record),
            AttributeType.SecurityDescriptor => new StructuredNtfsAttribute<SecurityDescriptor>(file, recordFile, record),
            AttributeType.Data => new NtfsAttribute(file, recordFile, record),
            AttributeType.Bitmap => new NtfsAttribute(file, recordFile, record),
            AttributeType.VolumeName => new StructuredNtfsAttribute<VolumeName>(file, recordFile, record),
            AttributeType.VolumeInformation => new StructuredNtfsAttribute<VolumeInformation>(file, recordFile, record),
            AttributeType.IndexRoot => new NtfsAttribute(file, recordFile, record),
            AttributeType.IndexAllocation => new NtfsAttribute(file, recordFile, record),
            AttributeType.ObjectId => new StructuredNtfsAttribute<ObjectId>(file, recordFile, record),
            AttributeType.ReparsePoint => new StructuredNtfsAttribute<ReparsePointRecord>(file, recordFile, record),
            AttributeType.AttributeList => new StructuredNtfsAttribute<AttributeList>(file, recordFile, record),
            _ => new NtfsAttribute(file, recordFile, record),
        };
    }

    public void SetExtent(FileRecordReference containingFile, AttributeRecord record)
    {
        _cachedRawBuffer = null;
        _containingFile = containingFile;
        _primaryRecord = record;
        _extents.Clear();
        _extents.Add(new AttributeReference(containingFile, record.AttributeId), record);
    }

    public void AddExtent(FileRecordReference containingFile, AttributeRecord record)
    {
        _cachedRawBuffer = null;
        _extents.Add(new AttributeReference(containingFile, record.AttributeId), record);
    }

    public void RemoveExtentCacheSafe(AttributeReference reference)
    {
        _extents.Remove(reference);
    }

    public bool ReplaceExtent(AttributeReference oldRef, AttributeReference newRef, AttributeRecord record)
    {
        _cachedRawBuffer = null;

        if (!_extents.Remove(oldRef))
        {
            return false;
        }

        if (oldRef.Equals(Reference) || _extents.Count == 0)
        {
            _primaryRecord = record;
            _containingFile = newRef.File;
        }

        _extents.Add(newRef, record);
        return true;
    }

    public IEnumerable<CookedDataRuns> GetCookedDataRuns()
    {
        foreach (var extent in _extents)
        {
            yield return extent.Value.GetCookedDataRuns();
        }
    }

    public IEnumerable<Range<long, long>> GetClusters()
    {
        foreach (var extent in _extents)
        {
            foreach (var cluster in extent.Value.GetClusters())
            {
                yield return cluster;
            }
        }
    }

    internal SparseStream Open(FileAccess access)
    {
        return new BufferStream(GetDataBuffer(), access);
    }

    internal IMappedBuffer GetDataBuffer()
    {
        return new NtfsAttributeBuffer(_file, this);
    }

    internal long OffsetToAbsolutePos(long offset)
    {
        return GetDataBuffer().MapPosition(offset);
    }

    public override string ToString() => $":{Name}:${AttributeTypeName}";
}