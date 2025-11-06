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
using System.Collections.Immutable;
using System.IO;
using System.Text;
using DiscUtils.Internal;
using DiscUtils.Streams;
using LTRData.Extensions.Buffers;

namespace DiscUtils.Wim;

internal class DirectoryEntry
{
    public FastDictionary<AlternateStreamEntry>? AlternateStreams;
    public FileAttributes Attributes;
    public long CreationTime;
    public string FileName = null!;
    public uint HardLink;
    public ImmutableArray<byte> Hash;
    public long LastAccessTime;
    public long LastWriteTime;
    public long Length;
    public uint ReparseTag;
    public uint SecurityId;
    public string? ShortName;
    public ushort StreamCount;
    public long SubdirOffset;

    public string SearchName
    {
        get
        {
            if (!FileName.Contains('.'))
            {
                return $"{FileName}.";
            }

            return FileName;
        }
    }

    public static DirectoryEntry? ReadFrom(DataReader reader)
    {
        var startPos = reader.Position;

        var length = reader.ReadInt64();
        if (length == 0)
        {
            return null;
        }

        var result = new DirectoryEntry
        {
            Length = length,
            Attributes = (FileAttributes)reader.ReadUInt32(),
            SecurityId = reader.ReadUInt32(),
            SubdirOffset = reader.ReadInt64()
        };
        reader.Skip(16);
        result.CreationTime = reader.ReadInt64();
        result.LastAccessTime = reader.ReadInt64();
        result.LastWriteTime = reader.ReadInt64();

        var hash = reader.ReadBytes(20);

        if (!BufferUtilities.IsAllZeros(hash))
        {
            result.Hash = hash.ToImmutableArray();
        }

        reader.Skip(4);
        result.ReparseTag = reader.ReadUInt32();
        result.HardLink = reader.ReadUInt32();
        result.StreamCount = reader.ReadUInt16();
        int shortNameLength = reader.ReadUInt16();
        int fileNameLength = reader.ReadUInt16();

        if (fileNameLength > 0)
        {
            result.FileName = Encoding.Unicode.GetString(reader.ReadBytes(fileNameLength + 2)).TrimEnd('\0');
        }
        else
        {
            result.FileName = string.Empty;
        }

        if (shortNameLength > 0)
        {
            result.ShortName = Encoding.Unicode.GetString(reader.ReadBytes(shortNameLength + 2)).TrimEnd('\0');
        }
        else
        {
            result.ShortName = null;
        }

        if (startPos + length > reader.Position)
        {
            var toRead = (int)(startPos + length - reader.Position);
            reader.Skip(toRead);
        }

        if (result.StreamCount > 0)
        {
            result.AlternateStreams = new FastDictionary<AlternateStreamEntry>(StringComparer.OrdinalIgnoreCase, entry => entry.Name);
            
            for (var i = 0; i < result.StreamCount; ++i)
            {
                var stream = AlternateStreamEntry.ReadFrom(reader);

                // Avoid crashes on badly built WIM files with multiple streams without
                // a stream name
                if (stream is not null && !result.AlternateStreams.Contains(stream.Name))
                {
                    result.AlternateStreams.Add(stream);

                    if (stream.Name == "" && BufferUtilities.IsAllZeros(result.Hash.AsSpan()))
                    {
                        result.Hash = stream.Hash;
                    }
                }
            }
        }

        return result;
    }

    public ImmutableArray<byte> GetStreamHash(string streamName)
    {
        if (string.IsNullOrEmpty(streamName))
        {
            if (!BufferUtilities.IsAllZeros(Hash.AsSpan()))
            {
                return Hash;
            }
        }

        if (AlternateStreams != null && AlternateStreams.TryGetValue(streamName, out var streamEntry))
        {
            return streamEntry.Hash;
        }

        return default;
    }

    internal long GetHeaderLength(string streamName)
    {
        if (string.IsNullOrEmpty(streamName))
        {
            return Length;
        }

        if (AlternateStreams != null && AlternateStreams.TryGetValue(streamName, out var streamEntry))
        {
            return streamEntry.Length;
        }

        throw new FileNotFoundException(
            $"No such alternate stream '{streamName}' in file '{FileName}'", $"{FileName}:{streamName}");
    }
}