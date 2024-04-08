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

namespace DiscUtils.Ntfs;

internal sealed class StandardInformation : IByteArraySerializable, IDiagnosticTraceable
{
    private bool _haveExtraFields = true;
    public uint ClassId;
    public DateTime CreationTime;
    public NtfsFileAttributes FileAttributes;
    public DateTime LastAccessTime;
    public uint MaxVersions;
    public DateTime MftChangedTime;
    public DateTime ModificationTime;
    public uint OwnerId;
    public ulong QuotaCharged;
    public uint SecurityId;
    public ulong UpdateSequenceNumber;
    public uint Version;

    public int Size
    {
        get { return _haveExtraFields ? 0x48 : 0x30; }
    }

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        CreationTime = ReadDateTime(buffer);
        ModificationTime = ReadDateTime(buffer.Slice(0x08));
        MftChangedTime = ReadDateTime(buffer.Slice(0x10));
        LastAccessTime = ReadDateTime(buffer.Slice(0x18));
        FileAttributes = (NtfsFileAttributes)EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x20));
        MaxVersions = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x24));
        Version = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x28));
        ClassId = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x2C));

        if (buffer.Length > 0x30)
        {
            OwnerId = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x30));
            SecurityId = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x34));
            QuotaCharged = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(0x38));
            UpdateSequenceNumber = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(0x40));
            _haveExtraFields = true;
            return 0x48;
        }

        _haveExtraFields = false;
        return 0x30;
    }

    public void WriteTo(Span<byte> buffer)
    {
        EndianUtilities.WriteBytesLittleEndian(CreationTime.ToFileTimeUtc(), buffer);
        EndianUtilities.WriteBytesLittleEndian(ModificationTime.ToFileTimeUtc(), buffer.Slice(0x08));
        EndianUtilities.WriteBytesLittleEndian(MftChangedTime.ToFileTimeUtc(), buffer.Slice(0x10));
        EndianUtilities.WriteBytesLittleEndian(LastAccessTime.ToFileTimeUtc(), buffer.Slice(0x18));
        EndianUtilities.WriteBytesLittleEndian((uint)FileAttributes, buffer.Slice(0x20));
        EndianUtilities.WriteBytesLittleEndian(MaxVersions, buffer.Slice(0x24));
        EndianUtilities.WriteBytesLittleEndian(Version, buffer.Slice(0x28));
        EndianUtilities.WriteBytesLittleEndian(ClassId, buffer.Slice(0x2C));

        if (_haveExtraFields)
        {
            EndianUtilities.WriteBytesLittleEndian(OwnerId, buffer.Slice(0x30));
            EndianUtilities.WriteBytesLittleEndian(SecurityId, buffer.Slice(0x34));
            EndianUtilities.WriteBytesLittleEndian(QuotaCharged, buffer.Slice(0x38));
            EndianUtilities.WriteBytesLittleEndian(UpdateSequenceNumber, buffer.Slice(0x40));
        }
    }

    public void Dump(TextWriter writer, string indent)
    {
        writer.WriteLine($"{indent}      Creation Time: {CreationTime}");
        writer.WriteLine($"{indent}  Modification Time: {ModificationTime}");
        writer.WriteLine($"{indent}   MFT Changed Time: {MftChangedTime}");
        writer.WriteLine($"{indent}   Last Access Time: {LastAccessTime}");
        writer.WriteLine($"{indent}   File Permissions: {FileAttributes}");
        writer.WriteLine($"{indent}       Max Versions: {MaxVersions}");
        writer.WriteLine($"{indent}            Version: {Version}");
        writer.WriteLine($"{indent}           Class Id: {ClassId}");
        writer.WriteLine($"{indent}        Security Id: {SecurityId}");
        writer.WriteLine($"{indent}      Quota Charged: {QuotaCharged}");
        writer.WriteLine($"{indent}     Update Seq Num: {UpdateSequenceNumber}");
    }

    public static StandardInformation InitializeNewFile(File file, NtfsFileAttributes flags)
    {
        var now = DateTime.UtcNow;

        var siStream = file.CreateStream(AttributeType.StandardInformation, null);
        var si = new StandardInformation
        {
            CreationTime = now,
            ModificationTime = now,
            MftChangedTime = now,
            LastAccessTime = now,
            FileAttributes = flags
        };
        siStream.SetContent(si);

        return si;
    }

    internal static FileAttributes ConvertFlags(NtfsFileAttributes flags, bool isDirectory)
    {
        var result = (FileAttributes)((uint)flags & 0xFFFF);

        if (isDirectory)
        {
            result |= System.IO.FileAttributes.Directory;
        }

        return result;
    }

    internal static NtfsFileAttributes SetFileAttributes(FileAttributes newAttributes, NtfsFileAttributes existing)
    {
        return (NtfsFileAttributes)(((uint)existing & 0xFFFF0000) | ((uint)newAttributes & 0xFFFF));
    }

    private static DateTime ReadDateTime(ReadOnlySpan<byte> buffer)
    {
        try
        {
            return DateTime.FromFileTimeUtc(EndianUtilities.ToInt64LittleEndian(buffer));
        }
        catch (ArgumentException)
        {
            return DateTime.FromFileTimeUtc(0);
        }
    }
}