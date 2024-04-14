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
using System.Text;
using DiscUtils.Internal;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Ntfs;

internal class FileNameRecord : IByteArraySerializable, IDiagnosticTraceable, IEquatable<FileNameRecord>
{
    public ulong AllocatedSize;
    public DateTime CreationTime;
    public uint EASizeOrReparsePointTag;
    public string FileName;
    public FileNameNamespace FileNameNamespace;
    public NtfsFileAttributes Flags;
    public DateTime LastAccessTime;
    public DateTime MftChangedTime;
    public DateTime ModificationTime;
    public FileRecordReference ParentDirectory;
    public ulong RealSize;

    public FileNameRecord() {}

    public FileNameRecord(FileNameRecord toCopy)
    {
        ParentDirectory = toCopy.ParentDirectory;
        CreationTime = toCopy.CreationTime;
        ModificationTime = toCopy.ModificationTime;
        MftChangedTime = toCopy.MftChangedTime;
        LastAccessTime = toCopy.LastAccessTime;
        AllocatedSize = toCopy.AllocatedSize;
        RealSize = toCopy.RealSize;
        Flags = toCopy.Flags;
        EASizeOrReparsePointTag = toCopy.EASizeOrReparsePointTag;
        FileNameNamespace = toCopy.FileNameNamespace;
        FileName = toCopy.FileName;
    }

    public FileAttributes FileAttributes => ConvertFlags(Flags);

    public int Size => 0x42 + FileName.Length * 2;

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        ParentDirectory = new FileRecordReference(EndianUtilities.ToUInt64LittleEndian(buffer));
        CreationTime = ReadDateTime(buffer.Slice(0x08));
        ModificationTime = ReadDateTime(buffer.Slice(0x10));
        MftChangedTime = ReadDateTime(buffer.Slice(0x18));
        LastAccessTime = ReadDateTime(buffer.Slice(0x20));
        AllocatedSize = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(0x28));
        RealSize = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(0x30));
        Flags = (NtfsFileAttributes)EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x38));
        EASizeOrReparsePointTag = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x3C));
        var fnLen = buffer[0x40];
        FileNameNamespace = (FileNameNamespace)buffer[0x41];
        FileName = Encoding.Unicode.GetString(buffer.Slice(0x42, fnLen * 2));

        return 0x42 + fnLen * 2;
    }

    public void WriteTo(Span<byte> buffer)
    {
        EndianUtilities.WriteBytesLittleEndian(ParentDirectory.Value, buffer);
        EndianUtilities.WriteBytesLittleEndian((ulong)CreationTime.ToFileTimeUtc(), buffer.Slice(0x08));
        EndianUtilities.WriteBytesLittleEndian((ulong)ModificationTime.ToFileTimeUtc(), buffer.Slice(0x10));
        EndianUtilities.WriteBytesLittleEndian((ulong)MftChangedTime.ToFileTimeUtc(), buffer.Slice(0x18));
        EndianUtilities.WriteBytesLittleEndian((ulong)LastAccessTime.ToFileTimeUtc(), buffer.Slice(0x20));
        EndianUtilities.WriteBytesLittleEndian(AllocatedSize, buffer.Slice(0x28));
        EndianUtilities.WriteBytesLittleEndian(RealSize, buffer.Slice(0x30));
        EndianUtilities.WriteBytesLittleEndian((uint)Flags, buffer.Slice(0x38));
        EndianUtilities.WriteBytesLittleEndian(EASizeOrReparsePointTag, buffer.Slice(0x3C));
        buffer[0x40] = (byte)FileName.Length;
        buffer[0x41] = (byte)FileNameNamespace;
        Encoding.Unicode.GetBytes(FileName.AsSpan(), buffer.Slice(0x42));
    }

    public void Dump(TextWriter writer, string indent)
    {
        writer.WriteLine($"{indent}FILE NAME RECORD");
        writer.WriteLine($"{indent}   Parent Directory: {ParentDirectory}");
        writer.WriteLine($"{indent}      Creation Time: {CreationTime}");
        writer.WriteLine($"{indent}  Modification Time: {ModificationTime}");
        writer.WriteLine($"{indent}   MFT Changed Time: {MftChangedTime}");
        writer.WriteLine($"{indent}   Last Access Time: {LastAccessTime}");
        writer.WriteLine($"{indent}     Allocated Size: {AllocatedSize}");
        writer.WriteLine($"{indent}          Real Size: {RealSize}");
        writer.WriteLine($"{indent}              Flags: {Flags}");

        if ((Flags & NtfsFileAttributes.ReparsePoint) != 0)
        {
            writer.WriteLine($"{indent}  Reparse Point Tag: {EASizeOrReparsePointTag}");
        }
        else
        {
            writer.WriteLine($"{indent}      Ext Attr Size: {EASizeOrReparsePointTag & 0xFFFF}");
        }

        writer.WriteLine($"{indent}          Namespace: {FileNameNamespace}");
        writer.WriteLine($"{indent}          File Name: {FileName}");
    }

    public override bool Equals(object obj) => obj is FileInfo fileInfo && Equals(fileInfo);

    public override int GetHashCode()
        => HashCode.Combine(ParentDirectory, FileNameNamespace, FileName);

    public bool Equals(FileNameRecord other)
    {
        if (other == null)
        {
            return false;
        }

        return ParentDirectory == other.ParentDirectory
               && FileNameNamespace == other.FileNameNamespace
               && FileName == other.FileName;
    }

    public override string ToString()
    {
        return FileName;
    }

    internal static NtfsFileAttributes SetAttributes(FileAttributes attrs, NtfsFileAttributes flags)
    {
        var attrMask = (FileAttributes)0xFFFF & ~FileAttributes.Directory;
        return (NtfsFileAttributes)(((uint)flags & 0xFFFF0000) | (uint)(attrs & attrMask));
    }

    internal static FileAttributes ConvertFlags(NtfsFileAttributes flags)
    {
        var result = (FileAttributes)((uint)flags & 0xFFFF);

        if ((flags & NtfsFileAttributes.Directory) != 0)
        {
            result |= FileAttributes.Directory;
        }

        return result;
    }

    private static DateTime ReadDateTime(ReadOnlySpan<byte> buffer)
    {
        try
        {
            return DateTime.FromFileTimeUtc(EndianUtilities.ToInt64LittleEndian(buffer));
        }
        catch (ArgumentException)
        {
            return DateTime.MinValue;
        }
    }
}