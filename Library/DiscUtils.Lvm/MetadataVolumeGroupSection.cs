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

using LTRData.Extensions.Buffers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal class MetadataVolumeGroupSection
{
    public string Name;
    public string Id;
    public ulong SequenceNumber;
    public string Format;
    public VolumeGroupStatus Status;
    public string[] Flags;
    public ulong ExtentSize;
    public ulong MaxLv;
    public ulong MaxPv;
    public ulong MetadataCopies;

    public MetadataPhysicalVolumeSection[] PhysicalVolumes;
    public MetadataLogicalVolumeSection[] LogicalVolumes;

    internal void Parse(ReadOnlyMemory<char> head, TextReader data)
    {
        Name = head.Span.Trim().TrimEnd('{').TrimEnd().ToString();

        for(; ;)
        {
            var lineStr = Metadata.ReadLine(data);

            if (lineStr == null)
            {
                break;
            }

            var line = lineStr.AsMemory();

            if (line.Span.IsWhiteSpace())
            {
                continue;
            }

            if (line.Span.Contains("=".AsSpan(), StringComparison.Ordinal))
            {
                var parameter = Metadata.ParseParameter(line);
                switch (parameter.Key.ToString().ToLowerInvariant())
                {
                    case "id":
                        Id = Metadata.ParseStringValue(parameter.Value.Span);
                        break;
                    case "seqno":
                        SequenceNumber = Metadata.ParseNumericValue(parameter.Value.Span);
                        break;
                    case "format":
                        Format = Metadata.ParseStringValue(parameter.Value.Span);
                        break;
                    case "status":
                        var values = Metadata.ParseArrayValue(parameter.Value.Span);
                        foreach (var value in values)
                        {
                            Status |= value.ToLowerInvariant().Trim() switch
                            {
                                "read" => VolumeGroupStatus.Read,
                                "write" => VolumeGroupStatus.Write,
                                "resizeable" => VolumeGroupStatus.Resizeable,
                                _ => throw new InvalidOperationException("Unexpected status in volume group metadata"),
                            };
                        }
                        break;
                    case "flags":
                        Flags = Metadata.ParseArrayValue(parameter.Value.Span);
                        break;
                    case "extent_size":
                        ExtentSize = Metadata.ParseNumericValue(parameter.Value.Span);
                        break;
                    case "max_lv":
                        MaxLv = Metadata.ParseNumericValue(parameter.Value.Span);
                        break;
                    case "max_pv":
                        MaxPv = Metadata.ParseNumericValue(parameter.Value.Span);
                        break;
                    case "metadata_copies":
                        MetadataCopies = Metadata.ParseNumericValue(parameter.Value.Span);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(parameter.Key.ToString(), "Unexpected parameter in volume group metadata");
                }
            }
            else if (line.Span.EndsWith("{".AsSpan(), StringComparison.Ordinal))
            {
                var sectionName = line.Span.TrimEnd('{').TrimEnd().ToString().ToLowerInvariant();
                switch (sectionName)
                {
                    case "physical_volumes":
                        PhysicalVolumes = ParsePhysicalVolumeSection(data).ToArray();
                        break;
                    case "logical_volumes":
                        LogicalVolumes = ParseLogicalVolumeSection(data).ToArray();

                        break;
                    default:
                        throw new ArgumentOutOfRangeException(sectionName, "Unexpected section in volume group metadata");
                }
            }
            else if (line.Span.EndsWith("}".AsSpan(), StringComparison.Ordinal))
            {
                break;
            }
        }
    }

    private static IEnumerable<MetadataLogicalVolumeSection> ParseLogicalVolumeSection(TextReader data)
    {
        string line;
        while ((line = Metadata.ReadLine(data)) != null)
        {
            if (line == String.Empty) continue;
            if (line.EndsWith('{'))
            {
                var pv = new MetadataLogicalVolumeSection();
                pv.Parse(line, data);
                yield return pv;
            }
            else if (line.EndsWith('}'))
            {
                break;
            }
        }
    }

    private static IEnumerable<MetadataPhysicalVolumeSection> ParsePhysicalVolumeSection(TextReader data)
    {
        string line;
        while ((line = Metadata.ReadLine(data)) != null)
        {
            if (line == String.Empty) continue;
            if (line.EndsWith('{'))
            {
                var pv = new MetadataPhysicalVolumeSection();
                pv.Parse(line, data);
                yield return pv;
            }
            else if (line.EndsWith('}'))
            {
                break;
            }
        }
    }
}

[Flags]
internal enum VolumeGroupStatus
{
    None = 0x0,
    Read = 0x1,
    Write = 0x2,
    Resizeable = 0x4,
}
