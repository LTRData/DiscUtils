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
using System.IO;
using DiscUtils.Streams;

namespace DiscUtils.Dmg;

internal sealed class DiskImageFile : VirtualDiskLayer
{
    private readonly Ownership _ownsStream;
    private readonly ResourceFork _resources;
    private Stream _stream;
    private readonly UdifResourceFile _udifHeader;

    /// <summary>
    /// Initializes a new instance of the DiskImageFile class.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="ownsStream">Indicates if the new instance should control the lifetime of the stream.</param>
    public DiskImageFile(Stream stream, Ownership ownsStream)
    {
        _udifHeader = new UdifResourceFile();
        _stream = stream;
        _ownsStream = ownsStream;

        stream.Position = stream.Length - _udifHeader.Size;
        Span<byte> data = stackalloc byte[_udifHeader.Size];
        
        stream.ReadExactly(data);

        _udifHeader.ReadFrom(data);

        if (_udifHeader.SignatureValid)
        {
            stream.Position = (long)_udifHeader.XmlOffset;
            var xmlData = stream.ReadExactly((int)_udifHeader.XmlLength);
            var plist = Plist.Parse(new MemoryStream(xmlData));

            _resources = ResourceFork.FromPlist(plist);
            Buffer = new UdifBuffer(stream, _resources, _udifHeader.SectorCount);
        }
    }

    public UdifBuffer Buffer { get; }

    /// <summary>
    /// Gets a value indicating whether the layer data is opened for writing.
    /// </summary>
    public override bool CanWrite => _stream.CanWrite;

    public override long Capacity => Buffer is null ? _stream.Length : Buffer.Capacity;

    /// <summary>
    /// Gets the geometry of the virtual disk layer.
    /// </summary>
    public override Geometry Geometry => Geometry.FromCapacity(Capacity);

    public override bool IsSparse => Buffer is not null;

    /// <summary>
    /// Gets a value indicating whether the file is a differencing disk.
    /// </summary>
    public override bool NeedsParent => false;

    public override FileLocator RelativeFileLocator => throw new NotImplementedException();

    public override SparseStream OpenContent(SparseStream parentStream, Ownership ownsStream)
    {
        if (parentStream is not null && ownsStream == Ownership.Dispose)
        {
            parentStream.Dispose();
        }

        if (Buffer is not null)
        {
            SparseStream rawStream = new BufferStream(Buffer, FileAccess.Read);
            return new BlockCacheStream(rawStream, Ownership.Dispose);
        }

        return SparseStream.FromStream(_stream, Ownership.None);
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing && _stream is not null && _ownsStream == Ownership.Dispose)
            {
                _stream.Dispose();
            }

            _stream = null;
        }
        finally
        {
            base.Dispose(disposing);
        }
    }
}