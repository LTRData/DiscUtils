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

using DiscUtils.Internal;
using DiscUtils.Streams;
using DiscUtils.Vfs;
using LTRData.Extensions.Buffers;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DiscUtils.Iso9660;

internal class ReaderDirectory : File, IVfsDirectory<ReaderDirEntry, File>
{
    private readonly FastDictionary<ReaderDirEntry> _entries;

    private readonly FastDictionary<ReaderDirEntry> _shortNames;

    public ReaderDirectory(IsoContext context, ReaderDirEntry dirEntry)
        : base(context, dirEntry)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(IsoUtilities.SectorSize);
        try
        {
            if (dirEntry.RecordExtents is not [var dirExtent])
            {
                throw new NotSupportedException("MultiExtent directory entries are not supported");
            }

            Array.Clear(buffer, 0, buffer.Length);
            Stream extent = new ExtentStream(_context.RawStream, dirExtent.LocationOfExtent, uint.MaxValue, 0, 0);

            var isCaseSensitive = false;

            _entries = new(StringComparer.OrdinalIgnoreCase, entry => entry.FileName);

            _shortNames = new(StringComparer.OrdinalIgnoreCase, entry => entry.ShortName);

            var totalLength = dirExtent.DataLength;
            uint totalRead = 0;
            while (totalRead < totalLength)
            {
                var bytesRead = (int)Math.Min(IsoUtilities.SectorSize, totalLength - totalRead);

                extent.ReadExactly(buffer, 0, bytesRead);
                totalRead += (uint)bytesRead;

                uint pos = 0;
                while (pos < bytesRead && buffer[pos] != 0)
                {
                    var length = (uint)DirectoryRecord.ReadFrom(buffer.AsSpan((int)pos), context.VolumeDescriptor.CharacterEncoding, out var dr);

                    if (!IsoUtilities.IsSpecialDirectory(dr))
                    {
                        var childDirEntry = new ReaderDirEntry(_context, dr);

                        if (_shortNames.TryGetValue(childDirEntry.ShortName, out var existingEntry))
                        {
                            if (existingEntry.Versions.FirstOrDefault(v => v.Version == childDirEntry.Version) is { } existingVersion)
                            {
                                existingVersion._records.Add(dr);
                            }
                            else
                            {
                                existingEntry.AddVersion(childDirEntry);
                            }
                        }
                        else
                        {
                            _shortNames.Add(childDirEntry);

                            if (context.SuspDetected && !string.IsNullOrEmpty(context.RockRidgeIdentifier))
                            {
                                if (childDirEntry.SuspRecords == null || !childDirEntry.SuspRecords.HasEntry(context.RockRidgeIdentifier, "RE"))
                                {
                                    // If there's already an entry with the same name but different case, we need to
                                    // switch to a case-sensitive dictionary

                                    if (!isCaseSensitive
                                        && _entries.TryGetValue(childDirEntry.FileName, out var existing)
                                        && existing.FileName != childDirEntry.FileName)
                                    {
                                        var newDict = new FastDictionary<ReaderDirEntry>(StringComparer.Ordinal, entry => entry.FileName);

                                        foreach (var entry in _entries)
                                        {
                                            newDict.Add(entry);
                                        }

                                        _entries.Clear();

                                        _entries = newDict;

                                        isCaseSensitive = true;

                                        _context.IsCaseSensitive = true;
                                    }
                                }
                            }

                            _entries.Add(childDirEntry);
                        }
                    }
                    else if (dr.FileIdentifier == "\0")
                    {
                        Self = new ReaderDirEntry(_context, dr);
                    }

                    pos += length;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public override byte[] SystemUseData => Self.RecordExtents[0].SystemUseData;

    public IReadOnlyDictionary<string, ReaderDirEntry> AllEntries => _entries;

    public ReaderDirEntry Self { get; }

    public ReaderDirEntry GetEntryByName(string name)
    {
        if (!_context.HideVersions)
        {
            return _entries.GetValueOrDefault(name)
                ?? _shortNames.GetValueOrDefault(name);
        }

        var verDelimiter = name.LastIndexOf(';');

        var namePart = name;
        uint? version = null;

        if (verDelimiter >= 0)
        {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
            if (!uint.TryParse(name.AsSpan(verDelimiter + 1), out var v))
#else
            if (!uint.TryParse(name.Substring(verDelimiter + 1), out var v))
#endif
            {
                throw new IOException($"Invalid version number in file name '{name}'");
            }

            version = v;

            namePart = name.Substring(0, verDelimiter);
        }

        if (_entries.TryGetValue(namePart, out var directEntry))
        {
            if (version.HasValue)
            {
                return directEntry.Versions.FirstOrDefault(v => v.Version == version);
            }

            return directEntry.Versions.MaxBy(v => v.Version);
        }

        if (_shortNames.TryGetValue(namePart, out var shortNameEntry))
        {
            if (version.HasValue)
            {
                return shortNameEntry.Versions.FirstOrDefault(v => v.Version == version);
            }

            return shortNameEntry.Versions.MaxBy(v => v.Version);
        }

        return null;
    }

    public ReaderDirEntry CreateNewFile(string name)
    {
        throw new NotSupportedException();
    }
}