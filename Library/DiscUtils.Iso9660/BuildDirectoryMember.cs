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
using System.Text;
using System.Linq;
using LTRData.Extensions.Split;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Iso9660;

/// <summary>
/// Provides the base class for <see cref="BuildFileInfo"/> and
/// <see cref="BuildDirectoryInfo"/> objects that will be built into an
/// ISO image.
/// </summary>
/// <remarks>Instances of this class have two names, a <see cref="Name"/>,
/// which is the full-length Joliet name and a <see cref="ShortName"/>,
/// which is the strictly compliant ISO 9660 name.</remarks>
public abstract class BuildDirectoryMember
{
    internal static readonly Comparer<BuildDirectoryMember> SortedComparison = new DirectorySortedComparison();

    /// <summary>
    /// Initializes a new instance of the BuildDirectoryMember class.
    /// </summary>
    /// <param name="name">The Joliet compliant name of the file or directory.</param>
    /// <param name="shortName">The ISO 9660 compliant name of the file or directory.</param>
    protected BuildDirectoryMember(string name, string shortName)
    {
        Name = name;
        ShortName = shortName;
        CreationTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets or sets the creation date for the file or directory, in UTC.
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// Gets the Joliet compliant name of the file or directory.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the parent directory, or <c>null</c> if this is the root directory.
    /// </summary>
    public abstract BuildDirectoryInfo Parent { get; }

    /// <summary>
    /// Gets the ISO 9660 compliant name of the file or directory.
    /// </summary>
    public string ShortName { get; }

    internal string PickName(string nameOverride, Encoding enc)
    {
        if (nameOverride != null)
        {
            return nameOverride;
        }

        return enc == Encoding.ASCII ? ShortName : Name;
    }

    internal abstract long GetDataSize(Encoding enc);

    internal uint GetDirectoryRecordSize(Encoding enc)
        => DirectoryRecord.CalcLength(PickName(null, enc), enc);

    public override string ToString()
        => Name ?? base.ToString();

    public override int GetHashCode()
        => HashCode.Combine(ShortName, ReferenceEquals(Parent, this) ? null : Parent);

    public override bool Equals(object obj)
        => obj is BuildDirectoryMember other && Equals(other);

    public bool Equals(BuildDirectoryMember other) =>
        StringComparer.OrdinalIgnoreCase.Equals(Name, other?.Name) &&
        StringComparer.OrdinalIgnoreCase.Equals(ShortName, other?.ShortName) &&
        CreationTime == other?.CreationTime &&
        ReferenceEquals(Parent, other?.Parent);

    private class DirectorySortedComparison : Comparer<BuildDirectoryMember>
    {
        public override int Compare(BuildDirectoryMember x, BuildDirectoryMember y)
        {
            var xParts = x.Name.AsMemory().Split('.', ';').ToArray();
            var yParts = y.Name.AsMemory().Split('.', ';').ToArray();

            ReadOnlyMemory<char> xPart;
            ReadOnlyMemory<char> yPart;

            for (var i = 0; i < 2; ++i)
            {
                xPart = xParts.Length > i ? xParts[i] : ReadOnlyMemory<char>.Empty;
                yPart = yParts.Length > i ? yParts[i] : ReadOnlyMemory<char>.Empty;
                var val = ComparePart(xPart.Span, yPart.Span, ' ');
                if (val != 0)
                {
                    return val;
                }
            }

            xPart = xParts.Length > 2 ? xParts[2] : ReadOnlyMemory<char>.Empty;
            yPart = yParts.Length > 2 ? yParts[2] : ReadOnlyMemory<char>.Empty;
            return ComparePartBackwards(xPart.Span, yPart.Span, '0');
        }

        private static int ComparePart(ReadOnlySpan<char> x, ReadOnlySpan<char> y, char padChar)
        {
            var max = Math.Max(x.Length, y.Length);
            for (var i = 0; i < max; ++i)
            {
                var xChar = i < x.Length ? x[i] : padChar;
                var yChar = i < y.Length ? y[i] : padChar;

                if (xChar != yChar)
                {
                    return xChar - yChar;
                }
            }

            return 0;
        }

        private static int ComparePartBackwards(ReadOnlySpan<char> x, ReadOnlySpan<char> y, char padChar)
        {
            var max = Math.Max(x.Length, y.Length);

            var xPad = max - x.Length;
            var yPad = max - y.Length;

            for (var i = 0; i < max; ++i)
            {
                var xChar = i >= xPad ? x[i - xPad] : padChar;
                var yChar = i >= yPad ? y[i - yPad] : padChar;

                if (xChar != yChar)
                {
                    // Note: Version numbers are in DESCENDING order!
                    return yChar - xChar;
                }
            }

            return 0;
        }
    }
}