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

using DiscUtils.Streams;
using System;
using System.Collections.Generic;
using System.Text;

namespace DiscUtils.Iscsi;

internal class TextBuffer
{
    private readonly List<KeyValuePair<string, string>> _records;

    public TextBuffer()
    {
        _records = [];
    }

    internal int Count => _records.Count;

    public string this[string key]
    {
        get
        {
            foreach (var entry in _records)
            {
                if (entry.Key == key)
                {
                    return entry.Value;
                }
            }

            return null;
        }

        set
        {
            for (var i = 0; i < _records.Count; ++i)
            {
                if (_records[i].Key == key)
                {
                    _records[i] = new KeyValuePair<string, string>(key, value);
                    return;
                }
            }

            _records.Add(new KeyValuePair<string, string>(key, value));
        }
    }

    public IEnumerable<KeyValuePair<string, string>> Lines => _records;

    public int Size
    {
        get
        {
            var i = 0;

            foreach (var entry in _records)
            {
                i += entry.Key.Length + entry.Value.Length + 2;
            }

            return i;
        }
    }

    public void Add(string key, string value)
    {
        _records.Add(new KeyValuePair<string, string>(key, value));
    }

    public void ReadFrom(byte[] buffer, int offset, int length)
        => ReadFrom(buffer.AsSpan(offset, length));

    public void ReadFrom(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        var end = 0 + buffer.Length;
        var i = 0;
        while (i < end)
        {
            var nameStart = i;
            while (i < end && buffer[i] != '=')
            {
                ++i;
            }

            if (i >= end)
            {
                throw new InvalidProtocolException("Invalid text buffer");
            }

            var name = Encoding.ASCII.GetString(buffer.Slice(nameStart, i - nameStart));

            ++i;
            var valueStart = i;
            while (i < end && buffer[i] != '\0')
            {
                ++i;
            }

            var value = Encoding.ASCII.GetString(buffer.Slice(valueStart, i - valueStart));
            ++i;

            Add(name, value);
        }
    }

    public int WriteTo(byte[] buffer, int offset)
    {
        var i = offset;

        foreach (var entry in _records)
        {
            i += Encoding.ASCII.GetBytes(entry.Key, 0, entry.Key.Length, buffer, i);
            buffer[i++] = (byte)'=';
            i += Encoding.ASCII.GetBytes(entry.Value, 0, entry.Value.Length, buffer, i);
            buffer[i++] = 0;
        }

        return i - offset;
    }

    internal void Remove(string key)
    {
        for (var i = 0; i < _records.Count; ++i)
        {
            if (_records[i].Key == key)
            {
                _records.RemoveAt(i);
                return;
            }
        }
    }
}