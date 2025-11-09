//
// Copyright (c) 2008-2024, Kenneth Bell, Olof Lagerkvist and contributors
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
using System.Linq;
using System.Text;
using DiscUtils.Streams;

namespace DiscUtils.Iso9660;

/// <summary>
/// Represents a directory that will be built into the ISO image.
/// </summary>
public sealed class BuildDirectoryInfo : BuildDirectoryMember
{
    internal static readonly Comparer<BuildDirectoryInfo> PathTableSortComparison = new PathTableComparison();

    private readonly Dictionary<string, BuildDirectoryMember> _membersLongNames;
    private readonly Dictionary<string, BuildDirectoryMember> _membersShortNames;

#if NET9_0_OR_GREATER
    private readonly Dictionary<string, BuildDirectoryMember>.AlternateLookup<ReadOnlySpan<char>> _membersLongNamesAltLookup;
    private readonly Dictionary<string, BuildDirectoryMember>.AlternateLookup<ReadOnlySpan<char>> _membersShortNamesAltLookup;
#endif

    private readonly BuildDirectoryInfo _parent;
    private List<BuildDirectoryMember> _sortedMembers;

    internal BuildDirectoryInfo(ReadOnlyMemory<char> name, BuildDirectoryInfo parent)
        : base(name.ToString(), MakeShortDirName(name, parent))
    {
        _parent = parent ?? this;
        HierarchyDepth = parent == null ? 0 : parent.HierarchyDepth + 1;
        _membersLongNames = new(StringComparer.OrdinalIgnoreCase);
        _membersShortNames = new(StringComparer.OrdinalIgnoreCase);

#if NET9_0_OR_GREATER
        _membersLongNamesAltLookup = _membersLongNames.GetAlternateLookup<ReadOnlySpan<char>>();
        _membersShortNamesAltLookup = _membersShortNames.GetAlternateLookup<ReadOnlySpan<char>>();
#endif
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
    internal bool TryGetMember(ReadOnlyMemory<char> name, out BuildDirectoryMember member)
    {
#if NET9_0_OR_GREATER
        return _membersLongNamesAltLookup.TryGetValue(name.Span, out member);
#else
        return _membersLongNames.TryGetValue(name.ToString(), out member);
#endif
    }

    /// <summary>
    /// Gets the specified child directory or file by short name.
    /// </summary>
    /// <param name="name">The short name of the file or directory to get.</param>
    /// <param name="member">The member found (or <c>null</c>).</param>
    /// <returns><c>true</c> if the specified member was found.</returns>
    internal bool TryGetMemberByShortName(ReadOnlyMemory<char> name, out BuildDirectoryMember member)
    {
#if NET9_0_OR_GREATER
        return _membersShortNamesAltLookup.TryGetValue(name.Span, out member);
#else
        return _membersShortNames.TryGetValue(name.ToString(), out member);
#endif
    }

    internal void Add(BuildDirectoryMember member)
    {
        _membersLongNames.Add(member.Name, member);
        _membersShortNames.Add(member.ShortName, member);
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

        return checked((uint)(8 + nameBytes + ((nameBytes & 0x1) == 1 ? 1 : 0)));
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

    private static string MakeShortDirName(ReadOnlyMemory<char> longName, BuildDirectoryInfo parent)
    {
        if (parent is null
            || (longName.Length <= 30
            && IsoUtilities.IsValidDirectoryName(longName.Span)
            && !parent.TryGetMemberByShortName(longName, out _)))
        {
            return longName.ToString();
        }

        if (longName.Length > 30)
        {
            longName = longName.Slice(0, 30);
        }

        Span<char> shortNameChars = stackalloc char[longName.Length];
        longName.Span.ToUpperInvariant(shortNameChars);

        for (var i = 0; i < shortNameChars.Length; ++i)
        {
            if (!IsoUtilities.IsValidDChar(shortNameChars[i]) && shortNameChars[i] != '.' && shortNameChars[i] != ';')
            {
                shortNameChars[i] = '_';
            }
        }

        var shortName = shortNameChars.ToString();

        for (var attempt = 0; attempt < int.MaxValue; attempt++)
        {
            if (attempt > 0)
            {
                var attemptStr = attempt.ToString(CultureInfo.InvariantCulture);

                if (shortName.Length + attemptStr.Length >= 30)
                {
                    shortName = shortName.Remove(shortName.Length - attemptStr.Length - 1);
                }

                shortName = $"{shortName}_{attemptStr}";
            }

            if (!parent.TryGetMemberByShortName(shortName.AsMemory(), out _))
            {
                return shortName;
            }
        }

        throw new ArgumentException($"Unable to construct a unique short name for '{longName}' in directory {parent.Name}");
    }

    private List<BuildDirectoryMember> GetSortedMembers()
    {
        if (_sortedMembers == null)
        {
#if NET7_0_OR_GREATER
            _sortedMembers = [.. _membersLongNames.Values.Order(SortedComparison)];
#else
            _sortedMembers = [.. _membersLongNames.Values.OrderBy(static v => v, SortedComparison)];
#endif
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