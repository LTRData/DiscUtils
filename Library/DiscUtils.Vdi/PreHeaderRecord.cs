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
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Vdi;

internal class PreHeaderRecord
{
    public const uint VdiSignature = 0xbeda107f;
    public const int Size = 72;

    public string FileInfo;
    public uint Signature;
    public FileVersion Version;

    public static PreHeaderRecord Initialized()
    {
        var result = new PreHeaderRecord
        {
            FileInfo = "<<< Sun xVM VirtualBox Disk Image >>>\n",
            Signature = VdiSignature,
            Version = new FileVersion(0x00010001)
        };
        return result;
    }

    public int Read(ReadOnlySpan<byte> buffer)
    {
        var latin1Encoding = EncodingUtilities.GetLatin1Encoding();

        FileInfo = latin1Encoding.GetString(buffer.Slice(0, 64)).TrimEnd('\0');
        Signature = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(64));
        Version = new FileVersion(EndianUtilities.ToUInt32LittleEndian(buffer.Slice(68)));
        return Size;
    }

    public void Read(Stream s)
    {
        Span<byte> buffer = stackalloc byte[Size];
        s.ReadExactly(buffer);
        Read(buffer);
    }

    public void Write(Stream s)
    {
        Span<byte> buffer = stackalloc byte[Size];
        Write(buffer);
        s.Write(buffer);
    }

    public void Write(Span<byte> buffer)
    {
        var latin1Encoding = EncodingUtilities.GetLatin1Encoding();

        latin1Encoding.GetBytes(FileInfo.AsSpan(), buffer.Slice(0, 64));
        EndianUtilities.WriteBytesLittleEndian(Signature, buffer.Slice(64));
        EndianUtilities.WriteBytesLittleEndian(Version.Value, buffer.Slice(68));
    }
}