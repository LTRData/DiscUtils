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
using System.Text;
using DiscUtils.Internal;
using DiscUtils.Streams;

namespace DiscUtils.Iso9660;

/// <summary>
/// Represents a directory that will be built into the ISO image.
/// </summary>
public sealed class BuildDirectoryInfo : BuildDirectoryMember
{
    internal static readonly Comparer<BuildDirectoryInfo> PathTableSortComparison = new PathTableComparison();
    private readonly FastDictionary<BuildDirectoryMember> _members;

    private readonly BuildDirectoryInfo _parent;
    private List<BuildDirectoryMember> _sortedMembers;

    internal BuildDirectoryInfo(string name, BuildDirectoryInfo parent)
        : base(name, MakeShortDirName(name, parent))
    {
        _parent = parent == null ? this : parent;
        HierarchyDepth = parent == null ? 0 : parent.HierarchyDepth + 1;
        _members = new(StringComparer.OrdinalIgnoreCase, entry => entry.Name);
    }

    internal int HierarchyDepth { get; }

    /// <summary>
    /// The parent directory, or <c>null</c> if none.
    /// </summary>
    public override BuildDirectoryInfo Parent => _parent;

    /// <summary>
    /// Gets the specified child directory or file.
    /// </summary>
    /// <param name="name">The name of the file or directory to get.</param>
    /// <param name="member">The member found (or <c>null</c>).</param>
    /// <returns><c>true</c> if the specified member was found.</returns>
    internal bool TryGetMember(string name, out BuildDirectoryMember member)
    {
        return _members.TryGetValue(name, out member);
    }

    internal void Add(BuildDirectoryMember member)
    {
        _members.Add(member);
        _sortedMembers = null;
    }

    internal override long GetDataSize(Encoding enc)
    {
        var sorted = GetSortedMembers();

        long total = 34 * 2; // Two pseudo entries (self & parent)

        foreach (var m in sorted)
        {
            var recordSize = m.GetDirectoryRecordSize(enc);

            // If this record would span a sector boundary, then the current sector is
            // zero-padded, and the record goes at the start of the next sector.
            if (total % IsoUtilities.SectorSize + recordSize > IsoUtilities.SectorSize)
            {
                var padLength = IsoUtilities.SectorSize - total % IsoUtilities.SectorSize;
                total += padLength;
            }

            total += recordSize;
        }

        return MathUtilities.RoundUp(total, IsoUtilities.SectorSize);
    }

    internal uint GetPathTableEntrySize(Encoding enc)
    {
        var nameBytes = enc.GetByteCount(PickName(null, enc));

        return (uint)(8 + nameBytes + ((nameBytes & 0x1) == 1 ? 1 : 0));
    }

    internal int Write(Span<byte> buffer, Dictionary<BuildDirectoryMember, uint> locationTable, Encoding enc)
    {
        var pos = 0;

        var sorted = GetSortedMembers();

        // Two pseudo entries, effectively '.' and '..'
        pos += WriteMember(this, "\0", Encoding.ASCII, buffer.Slice(pos), locationTable, enc);
        pos += WriteMember(_parent, "\x01", Encoding.ASCII, buffer.Slice(pos), locationTable, enc);

        foreach (var m in sorted)
        {
            var recordSize = m.GetDirectoryRecordSize(enc);

            if (pos % IsoUtilities.SectorSize + recordSize > IsoUtilities.SectorSize)
            {
                var padLength = IsoUtilities.SectorSize - pos % IsoUtilities.SectorSize;
                buffer.Slice(pos, padLength).Clear();
                pos += padLength;
            }

            pos += WriteMember(m, null, enc, buffer.Slice(pos), locationTable, enc);
        }

        // Ensure final padding data is zero'd
        var finalPadLength = MathUtilities.RoundUp(pos, IsoUtilities.SectorSize) - pos;
        buffer.Slice(pos, finalPadLength).Clear();

        return pos + finalPadLength;
    }

    private static int WriteMember(BuildDirectoryMember m, string nameOverride, Encoding nameEnc, Span<byte> buffer, Dictionary<BuildDirectoryMember, uint> locationTable, Encoding dataEnc)
    {
        var dr = new DirectoryRecord
        {
            FileIdentifier = m.PickName(nameOverride, nameEnc),
            LocationOfExtent = locationTable[m],
            DataLength = (uint)m.GetDataSize(dataEnc),
            RecordingDateAndTime = m.CreationTime,
            Flags = m is BuildDirectoryInfo ? FileFlags.Directory : FileFlags.None
        };
        return dr.WriteTo(buffer, nameEnc);
    }

    private static string MakeShortDirName(string longName, BuildDirectoryInfo dir)
    {
        if (IsoUtilities.IsValidDirectoryName(longName))
        {
            return longName;
        }

        var shortNameChars = longName.ToUpperInvariant().ToCharArray();
        for (var i = 0; i < shortNameChars.Length; ++i)
        {
            if (!IsoUtilities.IsValidDChar(shortNameChars[i]) && shortNameChars[i] != '.' && shortNameChars[i] != ';')
            {
                shortNameChars[i] = '_';
            }
        }

        return new string(shortNameChars);
    }

    private List<BuildDirectoryMember> GetSortedMembers()
    {
        if (_sortedMembers == null)
        {
            var sorted = new List<BuildDirectoryMember>(_members.Values);
            sorted.Sort(SortedComparison);
            _sortedMembers = sorted;
        }

        return _sortedMembers;
    }

    private class PathTableComparison : Comparer<BuildDirectoryInfo>
    {
        public override int Compare(BuildDirectoryInfo x, BuildDirectoryInfo y)
        {
            if (x.HierarchyDepth != y.HierarchyDepth)
            {
                return x.HierarchyDepth - y.HierarchyDepth;
            }

            if (x.Parent != y.Parent)
            {
                return Compare(x.Parent, y.Parent);
            }

            return CompareNames(x.Name, y.Name, ' ');
        }

        private static int CompareNames(string x, string y, char padChar)
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
    }
}