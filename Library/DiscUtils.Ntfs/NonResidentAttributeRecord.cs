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
using System.IO;
using System.Text;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Ntfs;

internal sealed class NonResidentAttributeRecord : AttributeRecord
{
    private const ushort DefaultCompressionUnitSize = 4;
    private ulong _compressedSize;
    private ushort _compressionUnitSize;
    private ulong _dataAllocatedSize;
    private ulong _dataRealSize;

    private ushort _dataRunsOffset;
    private ulong _initializedDataSize;
    private ulong _lastVCN;

    private ulong _startingVCN;

    public NonResidentAttributeRecord(ReadOnlySpan<byte> buffer, out int length)
    {
        Read(buffer, out length);
    }

    public NonResidentAttributeRecord(AttributeType type, string name, ushort id, AttributeFlags flags,
                                      long firstCluster, ulong numClusters, uint bytesPerCluster)
        : base(type, name, id, flags)
    {
        _nonResidentFlag = 1;
        DataRuns = [new DataRun(firstCluster, (long)numClusters, false)];
        _lastVCN = numClusters - 1;
        _dataAllocatedSize = bytesPerCluster * numClusters;
        _dataRealSize = bytesPerCluster * numClusters;
        _initializedDataSize = bytesPerCluster * numClusters;

        if ((flags & (AttributeFlags.Compressed | AttributeFlags.Sparse)) != 0)
        {
            _compressionUnitSize = DefaultCompressionUnitSize;
        }
    }

    public NonResidentAttributeRecord(AttributeType type, string name, ushort id, AttributeFlags flags,
                                      long startVcn, List<DataRun> dataRuns)
        : base(type, name, id, flags)
    {
        _nonResidentFlag = 1;
        DataRuns = dataRuns;
        _startingVCN = (ulong)startVcn;

        if ((flags & (AttributeFlags.Compressed | AttributeFlags.Sparse)) != 0)
        {
            _compressionUnitSize = DefaultCompressionUnitSize;
        }

        if (dataRuns != null && dataRuns.Count != 0)
        {
            _lastVCN = _startingVCN;
            foreach (var run in dataRuns)
            {
                _lastVCN += (ulong)run.RunLength;
            }

            _lastVCN -= 1;
        }
    }

    /// <summary>
    /// The amount of space occupied by the attribute (in bytes).
    /// </summary>
    public override long AllocatedLength
    {
        get => (long)_dataAllocatedSize;
        set => _dataAllocatedSize = (ulong)value;
    }

    public long CompressedDataSize
    {
        get => (long)_compressedSize;
        set => _compressedSize = (ulong)value;
    }

    /// <summary>
    /// Gets or sets the size of a compression unit (in clusters).
    /// </summary>
    public int CompressionUnitSize
    {
        get => 1 << _compressionUnitSize;
        set => _compressionUnitSize = (ushort)MathUtilities.Log2(value);
    }

    /// <summary>
    /// The amount of data in the attribute (in bytes).
    /// </summary>
    public override long DataLength
    {
        get => (long)_dataRealSize;
        set => _dataRealSize = (ulong)value;
    }

    public List<DataRun> DataRuns { get; private set; }

    /// <summary>
    /// The amount of initialized data in the attribute (in bytes).
    /// </summary>
    public override long InitializedDataLength
    {
        get => (long)_initializedDataSize;
        set => _initializedDataSize = (ulong)value;
    }

    public long LastVcn
    {
        get => (long)_lastVCN;
        set => _lastVCN = (ulong)value;
    }

    public override int Size
    {
        get
        {
            byte nameLength = 0;
            var nameOffset =
                (ushort)((Flags & (AttributeFlags.Compressed | AttributeFlags.Sparse)) != 0 ? 0x48 : 0x40);
            if (Name != null)
            {
                nameLength = (byte)Name.Length;
            }

            var dataOffset = (ushort)MathUtilities.RoundUp(nameOffset + nameLength * 2, 8);

            // Write out data first, since we know where it goes...
            var dataLen = 0;
            foreach (var run in DataRuns)
            {
                dataLen += run.Size;
            }

            dataLen++; // NULL terminator

            return MathUtilities.RoundUp(dataOffset + dataLen, 8);
        }
    }

    public override long StartVcn => (long)_startingVCN;

    public void ReplaceRun(DataRun oldRun, DataRun newRun)
    {
        var idx = DataRuns.IndexOf(oldRun);
        if (idx < 0)
        {
            throw new ArgumentException("Attempt to replace non-existant run", nameof(oldRun));
        }

        DataRuns[idx] = newRun;
    }

    public int RemoveRun(DataRun run)
    {
        var idx = DataRuns.IndexOf(run);
        if (idx < 0)
        {
            throw new ArgumentException("Attempt to remove non-existant run", nameof(run));
        }

        DataRuns.RemoveAt(idx);
        return idx;
    }

    public void InsertRun(DataRun existingRun, DataRun newRun)
    {
        var idx = DataRuns.IndexOf(existingRun);
        if (idx < 0)
        {
            throw new ArgumentException("Attempt to replace non-existant run", nameof(existingRun));
        }

        DataRuns.Insert(idx + 1, newRun);
    }

    public void InsertRun(int index, DataRun newRun)
    {
        DataRuns.Insert(index, newRun);
    }

    public override IEnumerable<Range<long, long>> GetClusters()
    {
        var cookedRuns = DataRuns;

        long start = 0;
        foreach (var run in cookedRuns)
        {
            if (!run.IsSparse)
            {
                start += run.RunOffset;
                yield return new Range<long, long>(start, run.RunLength);
            }
        }
    }

    public override IBuffer GetReadOnlyDataBuffer(INtfsContext context)
    {
        return new NonResidentDataBuffer(context, this);
    }

    public override CookedDataRuns GetCookedDataRuns() => new(DataRuns, this);

    public override int Write(Span<byte> buffer)
    {
        ushort headerLength = 0x40;
        if ((Flags & (AttributeFlags.Compressed | AttributeFlags.Sparse)) != 0)
        {
            headerLength += 0x08;
        }

        byte nameLength = 0;
        var nameOffset = headerLength;
        if (Name != null)
        {
            nameLength = (byte)Name.Length;
        }

        var dataOffset = (ushort)MathUtilities.RoundUp(headerLength + nameLength * 2, 8);

        // Write out data first, since we know where it goes...
        var dataLen = 0;
        foreach (var run in DataRuns)
        {
            dataLen += run.Write(buffer.Slice(dataOffset + dataLen));
        }

        buffer[dataOffset + dataLen] = 0; // NULL terminator
        dataLen++;

        var length = MathUtilities.RoundUp(dataOffset + dataLen, 8);

        EndianUtilities.WriteBytesLittleEndian((uint)_type, buffer.Slice(0x00));
        EndianUtilities.WriteBytesLittleEndian(length, buffer.Slice(0x04));
        buffer[0x08] = _nonResidentFlag;
        buffer[0x09] = nameLength;
        EndianUtilities.WriteBytesLittleEndian(nameOffset, buffer.Slice(0x0A));
        EndianUtilities.WriteBytesLittleEndian((ushort)_flags, buffer.Slice(0x0C));
        EndianUtilities.WriteBytesLittleEndian(_attributeId, buffer.Slice(0x0E));

        EndianUtilities.WriteBytesLittleEndian(_startingVCN, buffer.Slice(0x10));
        EndianUtilities.WriteBytesLittleEndian(_lastVCN, buffer.Slice(0x18));
        EndianUtilities.WriteBytesLittleEndian(dataOffset, buffer.Slice(0x20));
        EndianUtilities.WriteBytesLittleEndian(_compressionUnitSize, buffer.Slice(0x22));
        EndianUtilities.WriteBytesLittleEndian((uint)0, buffer.Slice(0x24)); // Padding
        EndianUtilities.WriteBytesLittleEndian(_dataAllocatedSize, buffer.Slice(0x28));
        EndianUtilities.WriteBytesLittleEndian(_dataRealSize, buffer.Slice(0x30));
        EndianUtilities.WriteBytesLittleEndian(_initializedDataSize, buffer.Slice(0x38));
        if ((Flags & (AttributeFlags.Compressed | AttributeFlags.Sparse)) != 0)
        {
            EndianUtilities.WriteBytesLittleEndian(_compressedSize, buffer.Slice(0x40));
        }

        if (Name != null)
        {
            Encoding.Unicode.GetBytes(Name.AsSpan(), buffer.Slice(nameOffset, nameLength * 2));
        }

        return length;
    }

    public AttributeRecord Split(int suggestedSplitIdx)
    {
        int splitIdx;
        if (suggestedSplitIdx <= 0 || suggestedSplitIdx >= DataRuns.Count)
        {
            splitIdx = DataRuns.Count / 2;
        }
        else
        {
            splitIdx = suggestedSplitIdx;
        }

        var splitVcn = (long)_startingVCN;
        long splitLcn = 0;
        for (var i = 0; i < splitIdx; ++i)
        {
            splitVcn += DataRuns[i].RunLength;
            splitLcn += DataRuns[i].RunOffset;
        }

        var newRecordRuns = new List<DataRun>();
        while (DataRuns.Count > splitIdx)
        {
            var run = DataRuns[splitIdx];

            DataRuns.RemoveAt(splitIdx);
            newRecordRuns.Add(run);
        }

        // Each extent has implicit start LCN=0, so have to make stored runs match reality.
        // However, take care not to stomp on 'sparse' runs that may be at the start of the
        // new extent (indicated by Zero run offset).
        for (var i = 0; i < newRecordRuns.Count; ++i)
        {
            if (!newRecordRuns[i].IsSparse)
            {
                newRecordRuns[i].RunOffset += splitLcn;
                break;
            }
        }

        _lastVCN = (ulong)splitVcn - 1;

        return new NonResidentAttributeRecord(_type, _name, 0, _flags, splitVcn, newRecordRuns);
    }

    public override void Dump(TextWriter writer, string indent)
    {
        base.Dump(writer, indent);
        writer.WriteLine($"{indent}     Starting VCN: {_startingVCN}");
        writer.WriteLine($"{indent}         Last VCN: {_lastVCN}");
        writer.WriteLine($"{indent}   Comp Unit Size: {_compressionUnitSize}");
        writer.WriteLine($"{indent}   Allocated Size: {_dataAllocatedSize}");
        writer.WriteLine($"{indent}        Real Size: {_dataRealSize}");
        writer.WriteLine($"{indent}   Init Data Size: {_initializedDataSize}");
        if ((Flags & (AttributeFlags.Compressed | AttributeFlags.Sparse)) != 0)
        {
            writer.WriteLine($"{indent}  Compressed Size: {_compressedSize}");
        }

        var runStr = string.Empty;

        foreach (var run in DataRuns)
        {
            runStr += $" {run}";
        }

        writer.WriteLine($"{indent}        Data Runs:{runStr}");
    }

    protected override void Read(ReadOnlySpan<byte> buffer, out int length)
    {
        DataRuns = null;

        base.Read(buffer, out length);

        _startingVCN = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(0x10));
        _lastVCN = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(0x18));
        _dataRunsOffset = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(0x20));
        _compressionUnitSize = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(0x22));
        _dataAllocatedSize = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(0x28));
        _dataRealSize = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(0x30));
        _initializedDataSize = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(0x38));

        if ((Flags & (AttributeFlags.Compressed | AttributeFlags.Sparse)) != 0 && _dataRunsOffset > 0x40)
        {
            _compressedSize = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(0x40));
        }

        DataRuns = [];
        int pos = _dataRunsOffset;
        while (pos < length)
        {
            var run = new DataRun();
            var len = run.Read(buffer.Slice(pos));

            // Length 1 means there was only a header byte (i.e. terminator)
            if (len == 1)
            {
                break;
            }

            DataRuns.Add(run);
            pos += len;
        }
    }
}