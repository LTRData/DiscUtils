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
using DiscUtils.Streams;

namespace DiscUtils.Iso9660;

internal sealed class SuspRecords
{
    private readonly Dictionary<string, Dictionary<string, List<SystemUseEntry>>> _records;

    public SuspRecords(IsoContext context, ReadOnlySpan<byte> data)
    {
        _records = [];

        var contEntry = Parse(context, data.Slice(context.SuspSkipBytes));
        while (contEntry != null)
        {
            context.DataStream.Position = contEntry.Block * (long)context.VolumeDescriptor.LogicalBlockSize +
                                          contEntry.BlockOffset;
            
            var contData = context.DataStream.ReadExactly((int)contEntry.Length);

            contEntry = Parse(context, contData);
        }
    }

    public static bool DetectSharingProtocol(ReadOnlySpan<byte> data)
    {
        if (data == null || data.Length < 7)
        {
            return false;
        }

        return data[0] == 83
               && data[1] == 80
               && data[2] == 7
               && data[3] == 1
               && data[4] == 0xBE
               && data[5] == 0xEF;
    }

    public List<SystemUseEntry> GetEntries(string extension, string name)
    {
        if (string.IsNullOrEmpty(extension))
        {
            extension = string.Empty;
        }

        if (!_records.TryGetValue(extension, out var extensionData))
        {
            return null;
        }

        if (extensionData.TryGetValue(name, out var result))
        {
            return result;
        }

        return null;
    }

    public T GetEntry<T>(string extension, string name)
        where T : SystemUseEntry
    {
        var entries = GetEntries(extension, name);
        if (entries == null)
        {
            return null;
        }

        foreach (T entry in entries)
        {
            return entry;
        }

        return null;
    }

    public bool HasEntry(string extension, string name)
    {
        var entries = GetEntries(extension, name);
        return entries != null && entries.Count != 0;
    }

    private ContinuationSystemUseEntry Parse(IsoContext context, ReadOnlySpan<byte> data)
    {
        ContinuationSystemUseEntry contEntry = null;
        SuspExtension extension = null;

        if (context.SuspExtensions != null && context.SuspExtensions.Count > 0)
        {
            extension = context.SuspExtensions[0];
        }

        var pos = 0;
        while (data.Length - pos > 4)
        {
            var entry = SystemUseEntry.Parse(data.Slice(pos), context.VolumeDescriptor.CharacterEncoding,
                extension, out var len);

            pos += len;

            if (entry == null)
            {
                // A null entry indicates SUSP parsing must terminate.
                // This will occur if a termination record is found,
                // or if there is a problem with the SUSP data.
                return contEntry;
            }

            switch (entry.Name)
            {
                case "CE":
                    contEntry = (ContinuationSystemUseEntry)entry;
                    break;

                case "ES":
                    var esEntry = (ExtensionSelectSystemUseEntry)entry;
                    extension = context.SuspExtensions[esEntry.SelectedExtension];
                    break;

                case "PD":
                    break;

                case "SP":
                case "ER":
                    StoreEntry(null, entry);
                    break;

                default:
                    StoreEntry(extension, entry);
                    break;
            }
        }

        return contEntry;
    }

    private void StoreEntry(SuspExtension extension, SystemUseEntry entry)
    {
        var extensionId = extension == null ? string.Empty : extension.Identifier;

        if (!_records.TryGetValue(extensionId, out var extensionEntries))
        {
            extensionEntries = [];
            _records.Add(extensionId, extensionEntries);
        }

        if (!extensionEntries.TryGetValue(entry.Name, out var entries))
        {
            entries = [];
            extensionEntries.Add(entry.Name, entries);
        }

        entries.Add(entry);
    }
}