//
// Copyright (c) 2016, Bianco Veigel
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

namespace DiscUtils.Lvm;

using System;
using System.IO;
using System.Collections.Generic;
using DiscUtils.Streams;
using System.Linq;
using System.Runtime.InteropServices;
using LTRData.Extensions.Buffers;

internal class MetadataLogicalVolumeSection
{
    public string Name;
    public string Id;
    public Guid Identity;
    public LogicalVolumeStatus Status;
    public string[] Flags;
    public string CreationHost;
    public DateTime CreationTime;
    public ulong SegmentCount;
    public List<MetadataSegmentSection> Segments;
    private Dictionary<string, PhysicalVolume> _pvs;
    private ulong _extentSize;
    internal void Parse(string head, TextReader data)
    {
        var segments = new List<MetadataSegmentSection>();
        Name = head.AsSpan().Trim().TrimEnd('{').TrimEnd().ToString();
        string line;
        Span<byte> guid = stackalloc byte[16];
        
        while ((line = Metadata.ReadLine(data)) != null)
        {
            if (line == String.Empty) continue;
            if (line.AsSpan().Contains("=".AsSpan(), StringComparison.Ordinal))
            {
                var parameter = Metadata.ParseParameter(line.AsMemory());
                switch (parameter.Key.ToString().ToLowerInvariant())
                {
                    case "id":
                        Id = Metadata.ParseStringValue(parameter.Value.Span);
                        EndianUtilities.StringToBytes(Id.Replace("-", String.Empty), guid);
                        // Mark it as a version 4 GUID
                        guid[7] = (byte)((guid[7] | 0x40) & 0x4f);
                        guid[8] = (byte)((guid[8] | 0x80) & 0xbf);
                        Identity = MemoryMarshal.Read<Guid>(guid);
                        break;
                    case "status":
                        var values = Metadata.ParseArrayValue(parameter.Value.Span);
                        foreach (var value in values)
                        {
                            Status |= value.ToLowerInvariant().Trim() switch
                            {
                                "read" => LogicalVolumeStatus.Read,
                                "write" => LogicalVolumeStatus.Write,
                                "visible" => LogicalVolumeStatus.Visible,
                                _ => throw new InvalidOperationException("Unexpected status in physical volume metadata"),
                            };
                        }
                        break;
                    case "flags":
                        Flags = Metadata.ParseArrayValue(parameter.Value.Span);
                        break;
                    case "creation_host":
                        CreationHost = Metadata.ParseStringValue(parameter.Value.Span);
                        break;
                    case "creation_time":
                        CreationTime = Metadata.ParseDateTimeValue(parameter.Value.Span);
                        break;
                    case "segment_count":
                        SegmentCount = Metadata.ParseNumericValue(parameter.Value.Span);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(parameter.Key.ToString(), "Unexpected parameter in global metadata");
                }
            }
            else if (line.EndsWith('{'))
            {
                var segment = new MetadataSegmentSection();
                segment.Parse(line, data);
                segments.Add(segment);
            }
            else if (line.EndsWith('}'))
            {
                break;
            }
            else
            {
                throw new ArgumentOutOfRangeException(line, "unexpected input");
            }
        }
        Segments = segments;
    }

    internal long ExtentCount
    {
        get
        {
            var length = 0L;
            foreach (var segment in Segments)
            {
                length += (long) segment.ExtentCount;
            }
            return length;
        }
    }

    public SparseStreamOpenDelegate Open(Dictionary<string, PhysicalVolume> availablePvs, ulong extentSize)
    {
        _pvs = availablePvs;
        _extentSize = extentSize;
        return Open;
    }

    private ConcatStream Open()
    {
        if ((Status & LogicalVolumeStatus.Read) == 0)
            throw new IOException("volume is not readable");

        var segments = new List<MetadataSegmentSection>();
        foreach (var segment in Segments)
        {
            if (segment.Type != SegmentType.Striped)
                throw new IOException("unsupported segment type");
            segments.Add(segment);
        }
        segments.Sort(CompareSegments);

        // Sanity Check...
        ulong pos = 0;
        foreach (var segment in segments)
        {
            if (segment.StartExtent != pos)
            {
                throw new IOException("Volume extents are non-contiguous");
            }

            pos += segment.ExtentCount;
        }

        var streams = new List<SparseStream>();
        foreach (var segment in segments)
        {
            streams.Add(OpenSegment(segment));
        }
        return new ConcatStream(Ownership.Dispose, streams);
    }

    private SubStream OpenSegment(MetadataSegmentSection segment)
    {
        if (segment.Stripes.Length != 1)
        {
            throw new IOException("invalid number of stripes");
        }
        var stripe = segment.Stripes[0];
        if (!_pvs.TryGetValue(stripe.PhysicalVolumeName, out var pv))
        {
            throw new IOException("missing pv");
        }
        if (pv.PvHeader.DiskAreas.Count != 1)
        {
            throw new IOException("invalid number od pv data areas");
        }
        var dataArea = pv.PvHeader.DiskAreas[0];
        var start = dataArea.Offset + (stripe.StartExtentNumber*_extentSize*PhysicalVolume.SECTOR_SIZE);
        var length = segment.ExtentCount*_extentSize*PhysicalVolume.SECTOR_SIZE;
        return new SubStream(pv.Content, Ownership.None, (long) start, (long)length);
    }

    private int CompareSegments(MetadataSegmentSection x, MetadataSegmentSection y)
    {
        if (x.StartExtent > y.StartExtent)
        {
            return 1;
        }
        else if (x.StartExtent < y.StartExtent)
        {
            return -1;
        }
        return 0;
    }
}

[Flags]
internal enum LogicalVolumeStatus
{
    None = 0x0,
    Read = 0x1,
    Write = 0x2,
    Visible = 0x4,
}
