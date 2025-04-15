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


using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using LTRData.Extensions.Buffers;

namespace DiscUtils.Lvm;
internal class MetadataSegmentSection
{
    public string Name;
    public ulong StartExtent;
    public ulong ExtentCount;
    public SegmentType Type;
    public ulong StripeCount;
    public MetadataStripe[] Stripes;

    internal void Parse(string head, TextReader data)
    {
        Name = head.Trim().TrimEnd('{').TrimEnd();
        string line;
        while ((line = Metadata.ReadLine(data)) != null)
        {
            if (line == String.Empty)
            {
                continue;
            }

            if (line.AsSpan().Contains("=".AsSpan(), StringComparison.Ordinal))
            {
                var parameter = Metadata.ParseParameter(line.AsMemory());
                var parameterValue = parameter.Value.Span;

                switch (parameter.Key.ToString().ToLowerInvariant())
                {
                    case "start_extent":
                        StartExtent = Metadata.ParseNumericValue(parameterValue);
                        break;
                    case "extent_count":
                        ExtentCount = Metadata.ParseNumericValue(parameterValue);
                        break;
                    case "type":
                        var value = Metadata.ParseStringValue(parameter.Value.Span);
                        switch (value)
                        {
                            case "striped":
                                Type = SegmentType.Striped;
                                break;
                            case "zero":
                                Type = SegmentType.Zero;
                                break;
                            case "error":
                                Type = SegmentType.Error;
                                break;
                            case "free":
                                Type = SegmentType.Free;
                                break;
                            case "snapshot":
                                Type = SegmentType.Snapshot;
                                break;
                            case "mirror":
                                Type = SegmentType.Mirror;
                                break;
                            case "raid1":
                                Type = SegmentType.Raid1;
                                break;
                            case "raid10":
                                Type = SegmentType.Raid10;
                                break;
                            case "raid4":
                                Type = SegmentType.Raid4;
                                break;
                            case "raid5":
                                Type = SegmentType.Raid5;
                                break;
                            case "raid5_la":
                                Type = SegmentType.Raid5La;
                                break;
                            case "raid5_ra":
                                Type = SegmentType.Raid5Ra;
                                break;
                            case "raid5_ls":
                                Type = SegmentType.Raid5Ls;
                                break;
                            case "raid5_rs":
                                Type = SegmentType.Raid5Rs;
                                break;
                            case "raid6":
                                Type = SegmentType.Raid6;
                                break;
                            case "raid6_zr":
                                Type = SegmentType.Raid6Zr;
                                break;
                            case "raid6_nr":
                                Type = SegmentType.Raid6Nr;
                                break;
                            case "raid6_nc":
                                Type = SegmentType.Raid6Nc;
                                break;
                            case "thin-pool":
                                Type = SegmentType.ThinPool;
                                break;
                            case "thin":
                                Type = SegmentType.Thin;
                                break;
                        }

                        break;
                    case "stripe_count":
                        StripeCount = Metadata.ParseNumericValue(parameterValue);
                        break;
                    case "stripes":
                        if (parameterValue.Equals("[", StringComparison.Ordinal))
                        {
                            // Multi-line section
                            Stripes = ParseMultiLineStripesSection(data).ToArray();
                        }
                        else if (parameterValue.StartsWith("[", StringComparison.Ordinal) && parameterValue.EndsWith("]", StringComparison.Ordinal))
                        {
                            // Single line section
                            // Exclude the brackets from the input
                            Stripes = ParseSinglelineStripesSection(parameterValue[1..^1]).ToArray();
                        }
                        else
                        {
                            throw new ArgumentException("Unsupported or invalid stripe format", line);
                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException(parameter.Key.ToString(), "Unexpected parameter in global metadata");
                }
            }
            else if (line.EndsWith('}'))
            {
                return;
            }
            else
            {
                throw new ArgumentOutOfRangeException(line, "unexpected input");
            }
        }
    }

    private static IEnumerable<MetadataStripe> ParseMultiLineStripesSection(TextReader data)
    {
        string line;
        while ((line = Metadata.ReadLine(data)) != null)
        {
            if (line == string.Empty)
            {
                continue;
            }

            if (line.EndsWith(']'))
            {
                yield break;
            }

            var metadataStripes = ParseSinglelineStripesSection(line);

            foreach (var metadataStripe in metadataStripes)
            {
                yield return metadataStripe;
            }
        }
    }

    private static List<MetadataStripe> ParseSinglelineStripesSection(ReadOnlySpan<char> data)
    {
        var metadataStripes = new List<MetadataStripe>();
        while (!data.IsEmpty)
        {
            // Find the first comma separating the pair
            var firstCommaIndex = data.IndexOf(',');
            if (firstCommaIndex == -1) break;

            // Extract the first value
            var volumeNameSpan = data.Slice(0, firstCommaIndex);
            data = data[(firstCommaIndex + 1)..];

            // Find the second comma separating the pair
            var secondCommaIndex = data.IndexOf(',');
            var extentNumberSpan = secondCommaIndex == -1
                ? data // Last value
                : data.Slice(0, secondCommaIndex);

            // Create and parse the MetadataStripe
            var metadataStripe = new MetadataStripe();
            metadataStripe.Parse(volumeNameSpan, extentNumberSpan);

            metadataStripes.Add(metadataStripe);

            // Move to the next pair
            data = secondCommaIndex == -1
                ? ReadOnlySpan<char>.Empty
                : data[(secondCommaIndex + 1)..];
        }

        return metadataStripes;
    }
}

[Flags]
internal enum SegmentType
{
    //$ lvm segtypes, man(8) lvm
    None,
    Striped,
    Zero,
    Error,
    Free,
    Snapshot,
    Mirror,
    Raid1,
    Raid10,
    Raid4,
    Raid5,
    Raid5La,
    Raid5Ra,
    Raid5Ls,
    Raid5Rs,
    Raid6,
    Raid6Zr,
    Raid6Nr,
    Raid6Nc,
    ThinPool,
    Thin,
}
