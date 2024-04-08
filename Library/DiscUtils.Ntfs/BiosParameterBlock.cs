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
using System.IO;
using System.Text;
using System.Runtime.InteropServices;

using System.Security.Cryptography;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Ntfs;

internal class BiosParameterBlock
{
    const string NTFS_OEM_ID = "NTFS    ";

    public byte BiosDriveNumber; // Value: 0x80 (first hard disk)
    public ushort BytesPerSector;
    public byte ChkDskFlags; // Value: 0x00
    public ushort FatRootEntriesCount; // Must be 0
    public ushort FatSize16; // Must be 0
    public uint HiddenSectors; // Value: 0x3F 0x00 0x00 0x00
    public byte Media; // Must be 0xF8
    public long MftCluster;
    public long MftMirrorCluster;
    public byte NumFats; // Must be 0
    public ushort NumHeads; // Value: 0xFF 0x00
    public string OemId;
    public byte PaddingByte; // Value: 0x00
    public byte RawIndexBufferSize;
    public byte RawMftRecordSize;
    public ushort ReservedSectors; // Must be 0
    public int SectorsPerCluster;
    public ushort SectorsPerTrack; // Value: 0x3F 0x00
    public byte SignatureByte; // Value: 0x80
    public ushort TotalSectors16; // Must be 0
    public uint TotalSectors32; // Must be 0
    public long TotalSectors64;
    public ulong VolumeSerialNumber;

    public int BytesPerCluster
    {
        get { return BytesPerSector * SectorsPerCluster; }
    }

    public int IndexBufferSize
    {
        get { return CalcRecordSize(RawIndexBufferSize); }
    }

    public int MftRecordSize
    {
        get { return CalcRecordSize(RawMftRecordSize); }
    }

    public void Dump(TextWriter writer, string linePrefix)
    {
        writer.WriteLine($"{linePrefix}BIOS PARAMETER BLOCK (BPB)");
        writer.WriteLine($"{linePrefix}                OEM ID: {OemId}");
        writer.WriteLine($"{linePrefix}      Bytes per Sector: {BytesPerSector}");
        writer.WriteLine($"{linePrefix}   Sectors per Cluster: {SectorsPerCluster}");
        writer.WriteLine($"{linePrefix}      Reserved Sectors: {ReservedSectors}");
        writer.WriteLine($"{linePrefix}                # FATs: {NumFats}");
        writer.WriteLine($"{linePrefix}    # FAT Root Entries: {FatRootEntriesCount}");
        writer.WriteLine($"{linePrefix}   Total Sectors (16b): {TotalSectors16}");
        writer.WriteLine($"{linePrefix}                 Media: {Media:X}h");
        writer.WriteLine($"{linePrefix}        FAT size (16b): {FatSize16}");
        writer.WriteLine($"{linePrefix}     Sectors per Track: {SectorsPerTrack}");
        writer.WriteLine($"{linePrefix}               # Heads: {NumHeads}");
        writer.WriteLine($"{linePrefix}        Hidden Sectors: {HiddenSectors}");
        writer.WriteLine($"{linePrefix}   Total Sectors (32b): {TotalSectors32}");
        writer.WriteLine($"{linePrefix}     BIOS Drive Number: {BiosDriveNumber}");
        writer.WriteLine($"{linePrefix}          Chkdsk Flags: {ChkDskFlags}");
        writer.WriteLine($"{linePrefix}        Signature Byte: {SignatureByte}");
        writer.WriteLine($"{linePrefix}   Total Sectors (64b): {TotalSectors64}");
        writer.WriteLine($"{linePrefix}       MFT Record Size: {RawMftRecordSize}");
        writer.WriteLine($"{linePrefix}     Index Buffer Size: {RawIndexBufferSize}");
        writer.WriteLine($"{linePrefix}  Volume Serial Number: {VolumeSerialNumber}");
    }

    internal static BiosParameterBlock Initialized(Geometry diskGeometry, int clusterSize, uint partitionStartLba,
                                                   long partitionSizeLba, int mftRecordSize, int indexBufferSize)
    {
        var bpb = new BiosParameterBlock
        {
            OemId = NTFS_OEM_ID,
            BytesPerSector = Sizes.Sector
        };
        bpb.SectorsPerCluster = clusterSize / bpb.BytesPerSector;
        bpb.ReservedSectors = 0;
        bpb.NumFats = 0;
        bpb.FatRootEntriesCount = 0;
        bpb.TotalSectors16 = 0;
        bpb.Media = 0xF8;
        bpb.FatSize16 = 0;
        bpb.SectorsPerTrack = (ushort)diskGeometry.SectorsPerTrack;
        bpb.NumHeads = (ushort)diskGeometry.HeadsPerCylinder;
        bpb.HiddenSectors = partitionStartLba;
        bpb.TotalSectors32 = 0;
        bpb.BiosDriveNumber = 0x80;
        bpb.ChkDskFlags = 0;
        bpb.SignatureByte = 0x80;
        bpb.PaddingByte = 0;
        bpb.TotalSectors64 = partitionSizeLba - 1;
        bpb.RawMftRecordSize = bpb.CodeRecordSize(mftRecordSize);
        bpb.RawIndexBufferSize = bpb.CodeRecordSize(indexBufferSize);
        bpb.VolumeSerialNumber = GenSerialNumber();

        return bpb;
    }

    internal static BiosParameterBlock FromBytes(ReadOnlySpan<byte> bytes)
    {
        var latin1Encoding = EncodingUtilities.GetLatin1Encoding();

        var bpb = new BiosParameterBlock
        {
            OemId = latin1Encoding.GetString(bytes.Slice(0x03, 8)),
            BytesPerSector = EndianUtilities.ToUInt16LittleEndian(bytes.Slice(0x0B)),
            TotalSectors16 = EndianUtilities.ToUInt16LittleEndian(bytes.Slice(0x13)),
            TotalSectors32 = EndianUtilities.ToUInt32LittleEndian(bytes.Slice(0x20)),
            SignatureByte = bytes[0x26],
            TotalSectors64 = EndianUtilities.ToInt64LittleEndian(bytes.Slice(0x28)),
            MftCluster = EndianUtilities.ToInt64LittleEndian(bytes.Slice(0x30)),
            RawMftRecordSize = bytes[0x40],
            SectorsPerCluster = DecodeSingleByteSize(bytes[0x0D])
        };
        if (!bpb.IsValid(long.MaxValue))
        {
            return bpb;
        }

        bpb.ReservedSectors = EndianUtilities.ToUInt16LittleEndian(bytes.Slice(0x0E));
        bpb.NumFats = bytes[0x10];
        bpb.FatRootEntriesCount = EndianUtilities.ToUInt16LittleEndian(bytes.Slice(0x11));
        bpb.Media = bytes[0x15];
        bpb.FatSize16 = EndianUtilities.ToUInt16LittleEndian(bytes.Slice(0x16));
        bpb.SectorsPerTrack = EndianUtilities.ToUInt16LittleEndian(bytes.Slice(0x18));
        bpb.NumHeads = EndianUtilities.ToUInt16LittleEndian(bytes.Slice(0x1A));
        bpb.HiddenSectors = EndianUtilities.ToUInt32LittleEndian(bytes.Slice(0x1C));
        bpb.BiosDriveNumber = bytes[0x24];
        bpb.ChkDskFlags = bytes[0x25];
        bpb.PaddingByte = bytes[0x27];
        bpb.MftMirrorCluster = EndianUtilities.ToInt64LittleEndian(bytes.Slice(0x38));
        bpb.RawIndexBufferSize = bytes[0x44];
        bpb.VolumeSerialNumber = EndianUtilities.ToUInt64LittleEndian(bytes.Slice(0x48));

        return bpb;
    }

    internal void ToBytes(Span<byte> buffer)
    {
        var latin1Encoding = EncodingUtilities.GetLatin1Encoding();

        latin1Encoding.GetBytes(OemId, buffer.Slice(0x03, 8));
        EndianUtilities.WriteBytesLittleEndian(BytesPerSector, buffer.Slice(0x0B));
        buffer[0x0D] = EncodeSingleByteSize(SectorsPerCluster);
        EndianUtilities.WriteBytesLittleEndian(ReservedSectors, buffer.Slice(0x0E));
        buffer[0x10] = NumFats;
        EndianUtilities.WriteBytesLittleEndian(FatRootEntriesCount, buffer.Slice(0x11));
        EndianUtilities.WriteBytesLittleEndian(TotalSectors16, buffer.Slice(0x13));
        buffer[0x15] = Media;
        EndianUtilities.WriteBytesLittleEndian(FatSize16, buffer.Slice(0x16));
        EndianUtilities.WriteBytesLittleEndian(SectorsPerTrack, buffer.Slice(0x18));
        EndianUtilities.WriteBytesLittleEndian(NumHeads, buffer.Slice(0x1A));
        EndianUtilities.WriteBytesLittleEndian(HiddenSectors, buffer.Slice(0x1C));
        EndianUtilities.WriteBytesLittleEndian(TotalSectors32, buffer.Slice(0x20));
        buffer[0x24] = BiosDriveNumber;
        buffer[0x25] = ChkDskFlags;
        buffer[0x26] = SignatureByte;
        buffer[0x27] = PaddingByte;
        EndianUtilities.WriteBytesLittleEndian(TotalSectors64, buffer.Slice(0x28));
        EndianUtilities.WriteBytesLittleEndian(MftCluster, buffer.Slice(0x30));
        EndianUtilities.WriteBytesLittleEndian(MftMirrorCluster, buffer.Slice(0x38));
        buffer[0x40] = RawMftRecordSize;
        buffer[0x44] = RawIndexBufferSize;
        EndianUtilities.WriteBytesLittleEndian(VolumeSerialNumber, buffer.Slice(0x48));
    }

    internal static int DecodeSingleByteSize(byte rawSize)
    {
        if (rawSize > 0x80)
        {
            return 1 << -(sbyte)rawSize;
        }
        
        return rawSize;
    }

    internal static byte EncodeSingleByteSize(int size)
    {
        if (size <= 0x80)
        {
            return (byte)size;
        }

        var count = 1;

        for (; size != 0; size >>= 1)
        {
            count--;
        }

        return (byte)(sbyte)count;
    }

    internal int CalcRecordSize(byte rawSize)
    {
        if (rawSize > 0x80)
        {
            return 1 << -(sbyte)rawSize;
        }
        
        return rawSize * SectorsPerCluster * BytesPerSector;
    }

    internal bool IsValidOemId()
    {
        return (!String.IsNullOrEmpty(OemId) && OemId.Length == NTFS_OEM_ID.Length
                && String.Compare(OemId, 0, NTFS_OEM_ID, 0, NTFS_OEM_ID.Length) == 0);
    }

    internal bool IsValid(long volumeSize)
    {
        /*
         * Some filesystem creation tools are not very strict and DO NOT
         * set the Signature byte to 0x80 (Version "8.0" NTFS BPB).
         *
         * Let's rather check OemId here, so we don't fail hard.
         */
        if (!IsValidOemId() || TotalSectors16 != 0 || TotalSectors32 != 0
            || TotalSectors64 == 0 || MftRecordSize == 0 || MftCluster == 0 || BytesPerSector == 0)
        {
            return false;
        }

        var mftPos = MftCluster * SectorsPerCluster * BytesPerSector;
        return mftPos < TotalSectors64 * BytesPerSector && mftPos < volumeSize;
    }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
    private static ulong GenSerialNumber()
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        RandomNumberGenerator.Fill(buffer);
        return MemoryMarshal.Read<ulong>(buffer);
    }
#else
    [ThreadStatic]
    private static RandomNumberGenerator rng;

    private static ulong GenSerialNumber()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(sizeof(ulong));
        try
        {
            rng ??= RandomNumberGenerator.Create();
            rng.GetBytes(buffer, 0, sizeof(ulong));
            return BitConverter.ToUInt64(buffer, 0);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
#endif

    private byte CodeRecordSize(int size)
    {
        if (size >= BytesPerCluster)
        {
            return (byte)(size / BytesPerCluster);
        }

        sbyte val = 0;
        while (size != 1)
        {
            size = (size >> 1) & 0x7FFFFFFF;
            val++;
        }

        return (byte)-val;
    }
}
