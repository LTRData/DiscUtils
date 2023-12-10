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

using System.Collections.Generic;
using System.IO;

namespace DiscUtils.Ntfs.Internals;

using Streams;

/// <summary>
/// An entry within the Master File Table.
/// </summary>
public sealed class MasterFileTableEntry
{
    private readonly INtfsContext _context;
    private readonly FileRecord _fileRecord;

    internal MasterFileTableEntry(INtfsContext context, FileRecord fileRecord)
    {
        _context = context;
        _fileRecord = fileRecord;
    }

    /// <summary>
    /// Gets the identity of the base entry for files split over multiple entries.
    /// </summary>
    /// <remarks>
    /// All entries that form part of the same file have the same value for
    /// this property.
    /// </remarks>
    public MasterFileTableReference BaseRecordReference
    {
        get { return new MasterFileTableReference(_fileRecord.BaseFile); }
    }

    /// <summary>
    /// Gets the flags indicating the nature of the entry.
    /// </summary>
    public MasterFileTableEntryFlags Flags
    {
        get { return (MasterFileTableEntryFlags)_fileRecord.Flags; }
    }

    /// <summary>
    /// Gets the number of hard links referencing this file.
    /// </summary>
    public int HardLinkCount
    {
        get { return _fileRecord.HardLinkCount; }
    }

    /// <summary>
    /// Gets the index of this entry in the Master File Table.
    /// </summary>
    public long Index
    {
        get { return _fileRecord.LoadedIndex; }
    }

    /// <summary>
    /// Gets the change identifier that is updated each time the file is modified by Windows, relates to the NTFS log file.
    /// </summary>
    /// <remarks>
    /// The NTFS log file provides journalling, preventing meta-data corruption in the event of a system crash.
    /// </remarks>
    public long LogFileSequenceNumber
    {
        get { return (long)_fileRecord.LogFileSequenceNumber; }
    }

    /// <summary>
    /// Gets the next attribute identity that will be allocated.
    /// </summary>
    public int NextAttributeId
    {
        get { return _fileRecord.NextAttributeId; }
    }

    /// <summary>
    /// Gets the index of this entry in the Master File Table (as stored in the entry itself).
    /// </summary>
    /// <remarks>
    /// Note - older versions of Windows did not store this value, so it may be Zero.
    /// </remarks>
    public long SelfIndex
    {
        get { return _fileRecord.MasterFileTableIndex; }
    }

    /// <summary>
    /// Gets the revision number of the entry.
    /// </summary>
    /// <remarks>
    /// Each time an entry is allocated or de-allocated, this number is incremented by one.
    /// </remarks>
    public int SequenceNumber
    {
        get { return _fileRecord.SequenceNumber; }
    }

    /// <summary>
    /// Gets the attributes contained in this entry.
    /// </summary>
    public ICollection<GenericAttribute> Attributes =>
        _fileRecord.Attributes
            .ConvertAll(attr => GenericAttribute.FromAttributeRecord(_context, attr))
            .AsReadOnly();

    public bool HasAttributes() => _fileRecord.Attributes.Count > 0;

    public bool HasAttributes(AttributeType type) =>
        _fileRecord.Attributes.FindIndex(attr => attr.AttributeType == type) >= 0;

    public IEnumerable<GenericAttribute> EnumerateAttributes(params AttributeType[] types)
    {
        foreach (var attr in _fileRecord.Attributes)
        {
            if (System.Array.Exists(types, t => t == attr.AttributeType))
            {
                GenericAttribute attribute = null;

                try
                {
                    attribute = GenericAttribute.FromAttributeRecord(_context, attr);
                }
                catch
                {
                }

                if (attribute != null)
                {
                    yield return attribute;
                }
            }
        }
    }

    public IEnumerable<GenericAttribute> EnumerateAttributes(AttributeType type)
    {
        foreach (var attr in _fileRecord.Attributes)
        {
            if (type == attr.AttributeType)
            {
                GenericAttribute attribute = null;

                try
                {
                    attribute = GenericAttribute.FromAttributeRecord(_context, attr);
                }
                catch
                {
                }

                if (attribute != null)
                {
                    yield return attribute;
                }
            }
        }
    }

    /// <summary>
    /// Opens an attribute as stream
    /// </summary>
    /// <param name="attr">Attribute to open</param>
    /// <param name="access">Access</param>
    public SparseStream Open(IAttributeLocator attr, FileAccess access) =>
        File.OpenStream(attr.Identifier, attr.AttributeType, access);

    public IEnumerable<Range<long, long>> GetClusters(IAttributeLocator attr) =>
        File.GetClusters(attr.Identifier, attr.AttributeType);

    public IEnumerable<CookedDataRuns> GetCookedDataRuns(IAttributeLocator attr) =>
        File.GetCookedDataRuns(attr.Identifier, attr.AttributeType);

    public long? GetInitializedDataSize(IAttributeLocator attr) =>
        File.GetInitializedDataSize(attr.Identifier, attr.AttributeType);

    public IBuffer GetRawBuffer(IAttributeLocator attr) =>
        File.GetRawBuffer(attr.Identifier, attr.AttributeType);

    private File _file;

    internal File File => _file ??= new File(_context, _fileRecord);

    public AttributeFlags GetAttributeFlags(IAttributeLocator attr)
    {
        var attribute = File.GetAttribute(attr.Identifier, attr.AttributeType);
        if (attribute != null)
        {
            return attribute.Flags;
        }

        throw new FileNotFoundException();
    }
}