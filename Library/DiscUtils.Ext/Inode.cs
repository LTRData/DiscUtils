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

namespace DiscUtils.Ext;

internal struct Inode : IByteArraySerializable
{
    public uint AccessTime;
    public uint BlocksCount;
    public uint CreationTime;
    public uint DeletionTime;
    public uint DirAcl;
    public uint[] DirectBlocks;
    public uint DoubleIndirectBlock;

    public ExtentBlock Extents;
    public byte[] FastSymlink;
    public uint FileAcl;
    public uint FileSize;
    public uint FileVersion;
    public InodeFlags Flags;
    public uint FragAddress;
    public byte Fragment;
    public byte FragmentSize;
    public ushort GroupIdHigh;
    public ushort GroupIdLow;
    public uint IndirectBlock;
    public ushort LinksCount;
    public ushort Mode;
    public uint ModificationTime;
    public uint TripleIndirectBlock;
    public ushort UserIdHigh;
    public ushort UserIdLow;

    public UnixFileType FileType => (UnixFileType)((Mode >> 12) & 0xff);

    public int Size => throw new NotImplementedException();

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        Mode = EndianUtilities.ToUInt16LittleEndian(buffer);
        UserIdLow = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(2));
        FileSize = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(4));
        AccessTime = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(8));
        CreationTime = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(12));
        ModificationTime = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(16));
        DeletionTime = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(20));
        GroupIdLow = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(24));
        LinksCount = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(26));
        BlocksCount = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(28));
        Flags = (InodeFlags)EndianUtilities.ToUInt32LittleEndian(buffer.Slice(32));

        FastSymlink = null;
        Extents = default;
        DirectBlocks = null;
        if (FileType == UnixFileType.Link && BlocksCount == 0)
        {
            FastSymlink = buffer.Slice(40, 60).ToArray();
        }
        else if ((Flags & InodeFlags.ExtentsUsed) != 0)
        {
            Extents = EndianUtilities.ToStruct<ExtentBlock>(buffer.Slice(40));
        }
        else
        {
            DirectBlocks = new uint[12];
            for (var i = 0; i < 12; ++i)
            {
                DirectBlocks[i] = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(40 + i * 4));
            }

            IndirectBlock = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(88));
            DoubleIndirectBlock = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(92));
            TripleIndirectBlock = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(96));
        }

        FileVersion = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(100));
        FileAcl = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(104));
        DirAcl = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(108));
        FragAddress = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(112));
        Fragment = buffer[116];
        FragmentSize = buffer[117];
        UserIdHigh = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(120));
        GroupIdHigh = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(122));

        return 128;
    }

    void IByteArraySerializable.WriteTo(Span<byte> buffer)
    {
        throw new NotImplementedException();
    }

    public IBuffer GetContentBuffer(Context context)
    {
        if (FastSymlink != null)
        {
            return new StreamBuffer(new MemoryStream(FastSymlink, false), Ownership.Dispose);
        }

        if ((Flags & InodeFlags.ExtentsUsed) != 0)
        {
            return new ExtentsFileBuffer(context, this);
        }

        return new FileBuffer(context, this);
    }
}