using System;
using System.Text;
using DiscUtils.ReFs.Checksums;
using DiscUtils.ReFs.Entries.Streams;
using LibraryTests.Core;

namespace LibraryTests.ReFs;

/// <summary>Known-answer tests for the reader's own checksum helpers (XXH64, SHA-256); the CRCs are tested in <see cref="CrcTest"/>.</summary>
public class ReFsHashingTest
{
    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    [Fact]
    public void XxHash64KnownAnswers()
    {
        Assert.Equal(0xEF46DB3751D8E999UL, Hashing.XxHash64(Array.Empty<byte>()));
        Assert.Equal(0x44BC2CF5AD770999UL, Hashing.XxHash64(Ascii("abc")));

        // Longer inputs: tail-only (13 bytes), one stripe plus tail (40 bytes) and many stripes (512 bytes).
        // Expected values come from an independent implementation of the XXH64 specification.
        static byte[] Sequence(int length)
        {
            var data = new byte[length];
            for (var i = 0; i < length; i++)
            {
                data[i] = (byte)i;
            }

            return data;
        }

        Assert.Equal(0x13D17C4C779723A8UL, Hashing.XxHash64(Sequence(13)));
        Assert.Equal(0xF5DA40F1B11741E9UL, Hashing.XxHash64(Sequence(40)));
        Assert.Equal(0x7B3BFCAAC0348AC0UL, Hashing.XxHash64(Sequence(512)));
    }

    [Fact]
    public void Sha256KnownAnswer()
    {
        Assert.Equal(
            "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
            System.BitConverter.ToString(Hashing.Sha256(Ascii("abc"))).Replace("-", string.Empty));
    }

    [Fact]
    public void Xxh64MetadataGivesXxh64FileData()
    {
        // Windows build 29683 (w2028pre-xxh64-integrity): data runs carry 8-byte XXH64 digests, one per block, as CRC and SHA-256 runs do.
        Assert.Equal(new StreamChecksumPolicy(ChecksumKind.Xxh64, 4096), StreamChecksumPolicy.Derive(ChecksumKind.Xxh64, 4096));
        Assert.Equal(new StreamChecksumPolicy(ChecksumKind.Xxh64, 16384), StreamChecksumPolicy.Derive(ChecksumKind.Xxh64, 65536));
    }
}