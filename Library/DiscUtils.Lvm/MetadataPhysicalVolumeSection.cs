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
using System.IO;

internal class MetadataPhysicalVolumeSection
{
    public string Name;
    public string Id;
    public string Device;
    public PhysicalVolumeStatus Status;
    public string[] Flags;
    public ulong DeviceSize;
    public ulong PeStart;
    public ulong PeCount;

    internal void Parse(string head, TextReader data)
    {
        Name = head.Trim().TrimEnd('{').TrimEnd();
        string line;
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
                        break;
                    case "device":
                        Device = Metadata.ParseStringValue(parameter.Value.Span);
                        break;
                    case "status":
                        var values = Metadata.ParseArrayValue(parameter.Value.Span);
                        foreach (var value in values)
                        {
                            Status |= value.ToLowerInvariant().Trim() switch
                            {
                                "read" => PhysicalVolumeStatus.Read,
                                "write" => PhysicalVolumeStatus.Write,
                                "allocatable" => PhysicalVolumeStatus.Allocatable,
                                _ => throw new InvalidOperationException("Unexpected status in physical volume metadata"),
                            };
                        }
                        break;
                    case "flags":
                        Flags = Metadata.ParseArrayValue(parameter.Value.Span);
                        break;
                    case "dev_size":
                        DeviceSize = Metadata.ParseNumericValue(parameter.Value.Span);
                        break;
                    case "pe_start":
                        PeStart = Metadata.ParseNumericValue(parameter.Value.Span);
                        break;
                    case "pe_count":
                        PeCount = Metadata.ParseNumericValue(parameter.Value.Span);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(parameter.Key.ToString(), "Unexpected parameter in global metadata");
                }
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
    }

}

[Flags]
internal enum PhysicalVolumeStatus
{
    None = 0x0,
    Read = 0x1,
    Write = 0x4,
    Allocatable = 0x8,
}
