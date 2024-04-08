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


using DiscUtils.Streams.Compatibility;
using LTRData.Extensions.Buffers;
using System;
using System.Collections.Generic;
using System.IO;

namespace DiscUtils.Lvm;
internal class Metadata
{
    public DateTime CreationTime;
    public string CreationHost;
    public string Description;
    public string Contents;
    public int Version;
    public List<MetadataVolumeGroupSection> VolumeGroupSections;
    private static readonly double _maxSeconds = DateTime.MaxValue.Subtract(DateTimeOffset.FromUnixTimeMilliseconds(0).DateTime).TotalSeconds;

    public static Metadata Parse(string metadata)
    {
        using var reader = new StringReader(metadata);
        var result = new Metadata();
        result.Parse(reader);
        return result;
    }

    private void Parse(TextReader data)
    {
        var vgSection = new List<MetadataVolumeGroupSection>();
        for(; ;)
        {
            var lineStr = ReadLine(data);

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
                var parameter = ParseParameter(line);
                switch (parameter.Key.ToString().ToLowerInvariant())
                {
                    case "contents":
                        Contents = ParseStringValue(parameter.Value.Span);
                        break;
                    case "version":
                        Version = (int) ParseNumericValue(parameter.Value.Span);
                        break;
                    case "description":
                        Description = ParseStringValue(parameter.Value.Span);
                        break;
                    case "creation_host":
                        CreationHost = ParseStringValue(parameter.Value.Span);
                        break;
                    case "creation_time":
                        CreationTime = ParseDateTimeValue(parameter.Value.Span);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(parameter.Key.ToString(), "Unexpected parameter in global metadata");
                }
            }
            else if (line.Span.EndsWith("{".AsSpan(), StringComparison.Ordinal))
            {
                var vg = new MetadataVolumeGroupSection();
                vg.Parse(line, data);
                vgSection.Add(vg);
            }
        }

        VolumeGroupSections = vgSection;
    }

    internal static string ReadLine(TextReader data)
    {
        var line = data.ReadLine();
        if (line == null)
        {
            return null;
        }

        return RemoveComment(line).Trim();
    }

    internal static string[] ParseArrayValue(ReadOnlySpan<char> value)
    {
        var values = value.Trim('[').Trim(']').ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = ParseStringValue(values[i].AsSpan());
        }

        return values;
    }

    internal static string ParseStringValue(ReadOnlySpan<char> value)
    {
        return value.Trim().Trim('"').ToString();
    }

    internal static DateTime ParseDateTimeValue(ReadOnlySpan<char> value)
    {
        var numeric = ParseNumericValue(value);
        if (numeric > _maxSeconds)
        {
            return DateTime.MaxValue;
        }

        return DateTimeOffset.FromUnixTimeSeconds((long)numeric).DateTime;
    }

    internal static ulong ParseNumericValue(ReadOnlySpan<char> value)
    {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
        return ulong.Parse(value.Trim());
#else
        return ulong.Parse(value.Trim().ToString());
#endif
    }

    internal static KeyValuePair<ReadOnlyMemory<char>, ReadOnlyMemory<char>> ParseParameter(ReadOnlyMemory<char> line)
    {
        var index = line.Span.IndexOf('=');
        if (index < 0)
        {
            throw new ArgumentException("invalid parameter line", nameof(line));
        }

        return new(key: line.Slice(0, index).Trim(), value: line.Slice(index + 1, line.Length - (index + 1)).Trim());
    }

    internal static string RemoveComment(string line)
    {
        var index = line.IndexOf('#');
        if (index < 0)
        {
            return line;
        }

        return line.Substring(0, index);
    }
}
