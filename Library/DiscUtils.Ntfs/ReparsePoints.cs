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
using System.Globalization;
using System.IO;
using DiscUtils.Streams;

namespace DiscUtils.Ntfs;

internal class ReparsePoints
{
    private readonly File _file;
    private readonly IndexView<Key, Data> _index;

    public ReparsePoints(File file)
    {
        _file = file;
        _index = new IndexView<Key, Data>(file.GetIndex("$R"));
    }

    internal void Add(uint tag, FileRecordReference file)
    {
        var newKey = new Key
        {
            Tag = tag,
            File = file
        };

        var data = new Data();

        _index[newKey] = data;
        _file.UpdateRecordInMft();
    }

    internal void Remove(uint tag, FileRecordReference file)
    {
        var key = new Key
        {
            Tag = tag,
            File = file
        };

        _index.Remove(key);
        _file.UpdateRecordInMft();
    }

    internal void Dump(TextWriter writer, string indent)
    {
        writer.WriteLine($"{indent}REPARSE POINT INDEX");

        foreach (var entry in _index.Entries)
        {
            writer.WriteLine($"{indent}  REPARSE POINT INDEX ENTRY");
            writer.WriteLine($"{indent}            Tag: {entry.Key.Tag:x}");
            writer.WriteLine($"{indent}  MFT Reference: {entry.Key.File}");
        }
    }

    internal struct Key : IByteArraySerializable
    {
        public FileRecordReference File;
        public uint Tag;

        public int Size => 12;

        public int ReadFrom(ReadOnlySpan<byte> buffer)
        {
            Tag = EndianUtilities.ToUInt32LittleEndian(buffer);
            File = new FileRecordReference(EndianUtilities.ToUInt64LittleEndian(buffer.Slice(4)));
            return 12;
        }

        public void WriteTo(Span<byte> buffer)
        {
            EndianUtilities.WriteBytesLittleEndian(Tag, buffer);
            EndianUtilities.WriteBytesLittleEndian(File.Value, buffer.Slice(4));
            ////Utilities.WriteBytesLittleEndian((uint)0, buffer, offset + 12);
        }

        public override string ToString() => $"{Tag:x}:{File}";
    }

    internal struct Data : IByteArraySerializable
    {
        public int Size => 0;

        public int ReadFrom(ReadOnlySpan<byte> buffer)
        {
            return 0;
        }

        public void WriteTo(Span<byte> buffer) {}

        public override string ToString()
        {
            return "<no data>";
        }
    }
}