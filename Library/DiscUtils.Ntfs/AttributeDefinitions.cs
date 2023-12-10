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
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Ntfs;

public sealed class AttributeDefinitions
{
    private readonly Dictionary<AttributeType, AttributeDefinitionRecord> _attrDefs;

    internal AttributeDefinitions()
    {
        _attrDefs = new Dictionary<AttributeType, AttributeDefinitionRecord>();

        Add(AttributeType.StandardInformation, "$STANDARD_INFORMATION", AttributeTypeFlags.MustBeResident, 0x30,
            0x48);
        Add(AttributeType.AttributeList, "$ATTRIBUTE_LIST", AttributeTypeFlags.CanBeNonResident, 0, -1);
        Add(AttributeType.FileName, "$FILE_NAME", AttributeTypeFlags.Indexed | AttributeTypeFlags.MustBeResident,
            0x44, 0x242);
        Add(AttributeType.ObjectId, "$OBJECT_ID", AttributeTypeFlags.MustBeResident, 0, 0x100);
        Add(AttributeType.SecurityDescriptor, "$SECURITY_DESCRIPTOR", AttributeTypeFlags.CanBeNonResident, 0x0, -1);
        Add(AttributeType.VolumeName, "$VOLUME_NAME", AttributeTypeFlags.MustBeResident, 0x2, 0x100);
        Add(AttributeType.VolumeInformation, "$VOLUME_INFORMATION", AttributeTypeFlags.MustBeResident, 0xC, 0xC);
        Add(AttributeType.Data, "$DATA", AttributeTypeFlags.None, 0, -1);
        Add(AttributeType.IndexRoot, "$INDEX_ROOT", AttributeTypeFlags.MustBeResident, 0, -1);
        Add(AttributeType.IndexAllocation, "$INDEX_ALLOCATION", AttributeTypeFlags.CanBeNonResident, 0, -1);
        Add(AttributeType.Bitmap, "$BITMAP", AttributeTypeFlags.CanBeNonResident, 0, -1);
        Add(AttributeType.ReparsePoint, "$REPARSE_POINT", AttributeTypeFlags.CanBeNonResident, 0, 0x4000);
        Add(AttributeType.ExtendedAttributesInformation, "$EA_INFORMATION", AttributeTypeFlags.MustBeResident, 0x8,
            0x8);
        Add(AttributeType.ExtendedAttributes, "$EA", AttributeTypeFlags.None, 0, 0x10000);
        Add(AttributeType.LoggedUtilityStream, "$LOGGED_UTILITY_STREAM", AttributeTypeFlags.CanBeNonResident, 0,
            0x10000);
    }

    internal AttributeDefinitions(File file)
    {
        _attrDefs = new Dictionary<AttributeType, AttributeDefinitionRecord>();

        using var s = file.OpenStream(AttributeType.Data, null, FileAccess.Read);
        Span<byte> buffer = stackalloc byte[AttributeDefinitionRecord.Size];
        while (s.ReadMaximum(buffer) == AttributeDefinitionRecord.Size)
        {
            var record = new AttributeDefinitionRecord();
            record.Read(buffer);

            // NULL terminator record
            if (record.Type != AttributeType.None)
            {
                _attrDefs.Add(record.Type, record);
            }
        }
    }

    internal void WriteTo(File file)
    {
        var attribs = new List<AttributeType>(_attrDefs.Keys);
        attribs.Sort();

        using var s = file.OpenStream(AttributeType.Data, null, FileAccess.ReadWrite);
        Span<byte> buffer = stackalloc byte[AttributeDefinitionRecord.Size];
        for (var i = 0; i < attribs.Count; ++i)
        {
            buffer.Clear();
            var attrDef = _attrDefs[attribs[i]];
            attrDef.Write(buffer);

            s.Write(buffer);
        }

        buffer.Clear();
        s.Write(buffer);
    }

    internal AttributeDefinitionRecord? Lookup(string name)
    {
        foreach (var record in _attrDefs.Values)
        {
            if (string.Equals(name, record.Name, StringComparison.OrdinalIgnoreCase))
            {
                return record;
            }
        }

        return null;
    }

    public AttributeType? FromString(string name)
    {
        foreach (var record in _attrDefs.Values)
        {
            if (string.Equals(name, record.Name, StringComparison.OrdinalIgnoreCase))
            {
                return record.Type;
            }
        }

        return null;
    }

    public bool MustBeResident(AttributeType attributeType)
    {
        if (_attrDefs.TryGetValue(attributeType, out var record))
        {
            return (record.Flags & AttributeTypeFlags.MustBeResident) != 0;
        }

        return false;
    }

    public bool IsIndexed(AttributeType attributeType)
    {
        if (_attrDefs.TryGetValue(attributeType, out var record))
        {
            return (record.Flags & AttributeTypeFlags.Indexed) != 0;
        }

        return false;
    }

    private void Add(AttributeType attributeType, string name, AttributeTypeFlags attributeTypeFlags, int minSize,
                     int maxSize)
    {
        var adr = new AttributeDefinitionRecord
        {
            Type = attributeType,
            Name = name,
            Flags = attributeTypeFlags,
            MinSize = minSize,
            MaxSize = maxSize
        };
        _attrDefs.Add(attributeType, adr);
    }

    public string ToString(AttributeType attributeType)
    {
        if (_attrDefs.TryGetValue(attributeType, out var record))
        {
            return record.Name;
        }
        return attributeType.ToString();
    }
}