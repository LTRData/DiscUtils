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
using System.Globalization;
using System.IO;
using System.Linq;

namespace DiscUtils.Vmdk;

internal class ExtentDescriptor
{
    public ExtentDescriptor() {}

    public ExtentDescriptor(ExtentAccess access, long size, ExtentType type, string fileName, long offset)
    {
        Access = access;
        SizeInSectors = size;
        Type = type;
        FileName = fileName;
        Offset = offset;
    }

    public ExtentAccess Access { get; private set; }

    public string FileName { get; private set; }

    public long Offset { get; private set; }

    public long SizeInSectors { get; private set; }

    public ExtentType Type { get; private set; }

    public static ExtentDescriptor Parse(ReadOnlyMemory<char> descriptor)
    {
        var elems = SplitQuotedString(descriptor).ToArray();
        if (elems.Length < 4)
        {
            throw new IOException($"Invalid extent descriptor: {descriptor}");
        }

        var result = new ExtentDescriptor
        {
            Access = ParseAccess(elems[0].Span),
            SizeInSectors = ParseLong(elems[1].Span),
            Type = ParseType(elems[2].Span),
            FileName = elems[3].Span.Trim('\"').ToString()
        };
        if (elems.Length > 4)
        {
            result.Offset = ParseLong(elems[4].Span);
        }

        return result;
    }

    public static long ParseLong(ReadOnlySpan<char> chars)
    {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
        return long.Parse(chars, NumberStyles.None, CultureInfo.InvariantCulture);
#else
        return long.Parse(chars.ToString(), NumberStyles.None, CultureInfo.InvariantCulture);
#endif
    }

    public static ExtentAccess ParseAccess(ReadOnlySpan<char> access)
    {
        if (access.Equals("NOACCESS".AsSpan(), StringComparison.Ordinal))
        {
            return ExtentAccess.None;
        }

        if (access.Equals("RDONLY".AsSpan(), StringComparison.Ordinal))
        {
            return ExtentAccess.ReadOnly;
        }

        if (access.Equals("RW".AsSpan(), StringComparison.Ordinal))
        {
            return ExtentAccess.ReadWrite;
        }

        throw new ArgumentException("Unknown access type", nameof(access));
    }

    public static string FormatAccess(ExtentAccess access)
    {
        return access switch
        {
            ExtentAccess.None => "NOACCESS",
            ExtentAccess.ReadOnly => "RDONLY",
            ExtentAccess.ReadWrite => "RW",
            _ => throw new ArgumentException("Unknown access type", nameof(access)),
        };
    }

    public static ExtentType ParseType(ReadOnlySpan<char> type)
    {
        if (type.Equals("FLAT".AsSpan(), StringComparison.Ordinal))
        {
            return ExtentType.Flat;
        }

        if (type.Equals("SPARSE".AsSpan(), StringComparison.Ordinal))
        {
            return ExtentType.Sparse;
        }

        if (type.Equals("ZERO".AsSpan(), StringComparison.Ordinal))
        {
            return ExtentType.Zero;
        }

        if (type.Equals("VMFS".AsSpan(), StringComparison.Ordinal))
        {
            return ExtentType.Vmfs;
        }

        if (type.Equals("VMFSSPARSE".AsSpan(), StringComparison.Ordinal))
        {
            return ExtentType.VmfsSparse;
        }

        if (type.Equals("VMFSRDM".AsSpan(), StringComparison.Ordinal))
        {
            return ExtentType.VmfsRdm;
        }

        if (type.Equals("VMFSRAW".AsSpan(), StringComparison.Ordinal))
        {
            return ExtentType.VmfsRaw;
        }

        if (type.Equals("SESPARSE".AsSpan(), StringComparison.Ordinal))
        {
            return ExtentType.SeSparse;
        }

        if (type.Equals("VSANSPARSE".AsSpan(), StringComparison.Ordinal))
        {
            return ExtentType.VsanSparse;
        }

        throw new ArgumentException("Unknown extent type", nameof(type));
    }

    public static string FormatExtentType(ExtentType type)
    {
        return type switch
        {
            ExtentType.Flat => "FLAT",
            ExtentType.Sparse => "SPARSE",
            ExtentType.Zero => "ZERO",
            ExtentType.Vmfs => "VMFS",
            ExtentType.VmfsSparse => "VMFSSPARSE",
            ExtentType.VmfsRdm => "VMFSRDM",
            ExtentType.VmfsRaw => "VMFSRAW",
            ExtentType.SeSparse => "SESPARSE",
            ExtentType.VsanSparse => "VSANSPARSE",
            _ => throw new ArgumentException("Unknown extent type", nameof(type)),
        };
    }

    public override string ToString()
    {
        var basic = $"{FormatAccess(Access)} {SizeInSectors} {FormatExtentType(Type)} \"{FileName}\"";
        if (Type != ExtentType.Sparse && Type != ExtentType.VmfsSparse && Type != ExtentType.Zero)
        {
            return $"{basic} {Offset}";
        }

        return basic;
    }

    private static IEnumerable<ReadOnlyMemory<char>> SplitQuotedString(ReadOnlyMemory<char> source)
    {
        var idx = 0;
        while (idx < source.Length)
        {
            // Skip spaces
            while (source.Span[idx] == ' ' && idx < source.Length)
            {
                idx++;
            }

            if (source.Span[idx] == '"')
            {
                // A quoted value, find end of quotes...
                var start = idx;
                idx++;
                while (idx < source.Length && source.Span[idx] != '"')
                {
                    idx++;
                }

                yield return source.Slice(start, idx - start + 1);
            }
            else
            {
                // An unquoted value, find end of value
                var start = idx;
                idx++;
                while (idx < source.Length && source.Span[idx] != ' ')
                {
                    idx++;
                }

                yield return source.Slice(start, idx - start);
            }

            idx++;
        }
    }
}