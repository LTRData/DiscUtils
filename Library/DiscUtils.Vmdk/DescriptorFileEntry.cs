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

using DiscUtils.Streams.Compatibility;
using LTRData.Extensions.Buffers;
using LTRData.Extensions.Split;
using System;
using System.Linq;

namespace DiscUtils.Vmdk;

internal class DescriptorFileEntry
{
    private readonly DescriptorFileEntryType _type;

    public DescriptorFileEntry(string key, string value, DescriptorFileEntryType type)
    {
        Key = key;
        Value = value;
        _type = type;
    }

    public string Key { get; }

    public string Value { get; set; }

    public static DescriptorFileEntry Parse(string value)
    {
        var parts = value.AsMemory().TokenEnum('=').Take(2).ToArray();

        for (var i = 0; i < parts.Length; ++i)
        {
            parts[i] = parts[i].Trim();
        }

        if (parts.Length > 1)
        {
            if (parts[1].Span.StartsWith("\"".AsSpan(), StringComparison.Ordinal))
            {
                return new DescriptorFileEntry(parts[0].ToString(), parts[1].Trim('\"').ToString(), DescriptorFileEntryType.Quoted);
            }

            return new DescriptorFileEntry(parts[0].ToString(), parts[1].ToString(), DescriptorFileEntryType.Plain);
        }

        return new DescriptorFileEntry(parts[0].ToString(), string.Empty, DescriptorFileEntryType.NoValue);
    }

    public override string ToString()
    {
        return ToString(true);
    }

    public string ToString(bool spaceOut)
    {
        // VMware workstation appears to be sensitive to spaces, wants them for 'header' values, not for DiskDataBase...
        var sep = spaceOut ? " " : string.Empty;

        return _type switch
        {
            DescriptorFileEntryType.NoValue => Key,
            DescriptorFileEntryType.Plain => $"{Key}{sep}={sep}{Value}",
            DescriptorFileEntryType.Quoted => $"{Key}{sep}={sep}\"{Value}\"",
            _ => throw new InvalidOperationException($"Unknown type: {_type}"),
        };
    }
}