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
using System.Linq;
using DiscUtils.Streams;

namespace DiscUtils.Ntfs;

internal readonly struct NtfsStream
{
    private readonly File _file;

    public NtfsStream(File file, NtfsAttribute attr)
    {
        _file = file;
        Attribute = attr;
    }

    public NtfsAttribute Attribute { get; }

    public AttributeType AttributeType
    {
        get { return Attribute.Type; }
    }

    public string Name
    {
        get { return Attribute.Name; }
    }

    /// <summary>
    /// Gets the content of a stream.
    /// </summary>
    /// <typeparam name="T">The stream's content structure.</typeparam>
    /// <returns>The content.</returns>
    public T GetContent<T>()
        where T : IByteArraySerializable, IDiagnosticTraceable, new()
    {
        var buffer = GetContent();
        var value = new T();
        value.ReadFrom(buffer);
        return value;
    }

    /// <summary>
    /// Gets the content of a stream as a byte array.
    /// </summary>
    /// <returns>The content.</returns>
    public byte[] GetContent()
    {
        using var s = Open(FileAccess.Read);

        var buffer = s.ReadExactly((int)s.Length);

        return buffer;
    }

    /// <summary>
    /// Sets the content of a stream.
    /// </summary>
    /// <typeparam name="T">The stream's content structure.</typeparam>
    /// <param name="value">The new value for the stream.</param>
    public void SetContent<T>(T value)
        where T : IByteArraySerializable, IDiagnosticTraceable, new()
    {
        byte[] allocated = null;

        var buffer = value.Size <= 1024
            ? stackalloc byte[value.Size]
            : (allocated = ArrayPool<byte>.Shared.Rent(value.Size)).AsSpan(0, value.Size);

        try
        {
            value.WriteTo(buffer);

            SetContent(buffer);
        }
        finally
        {
            if (allocated is not null)
            {
                ArrayPool<byte>.Shared.Return(allocated);
            }
        }
    }

    /// <summary>
    /// Sets the content of a stream.
    /// </summary>
    /// <param name="content">The new value for the stream.</param>
    public void SetContent(ReadOnlySpan<byte> content)
    {
        using var s = Open(FileAccess.Write);
        s.Write(content);
        s.SetLength(content.Length);
    }

    public SparseStream Open(FileAccess access)
    {
        return Attribute.Open(access);
    }

    public IEnumerable<Range<long, long>> GetClusters()
    {
        return Attribute.GetClusters();
    }

    public IEnumerable<StreamExtent> GetAbsoluteExtents()
    {
        long clusterSize = _file.Context.BiosParameterBlock.BytesPerCluster;
        if (Attribute.IsNonResident)
        {
            var clusters = Attribute.GetClusters();
            foreach (var clusterRange in clusters)
            {
                yield return new StreamExtent(clusterRange.Offset * clusterSize, clusterRange.Count * clusterSize);
            }
        }
        else
        {
            yield return new StreamExtent(Attribute.OffsetToAbsolutePos(0), Attribute.Length);
        }
    }

    public long GetAllocatedClustersCount()
    {
        if (Attribute.IsNonResident)
        {
            var clusters = Attribute.GetClusters().Sum(clusterRange => clusterRange.Count);
            return clusters;
        }
        else
        {
            return 0;
        }
    }
}
