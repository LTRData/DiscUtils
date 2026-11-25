using System;
using System.Text;
using DiscUtils.Internal;

namespace LibraryTests.Core;

/// <summary>Known answers ("123456789" check values from the CRC catalogue) for the CRC-32 and CRC-64 classes in DiscUtils.Core.</summary>
public class CrcTest
{
    private static readonly byte[] Check = Encoding.ASCII.GetBytes("123456789");

    [Theory]
    [InlineData((int)Crc32Algorithm.Common, 0xCBF43926u)]
    [InlineData((int)Crc32Algorithm.Castagnoli, 0xE3069283u)]
    public void Crc32LittleEndianKnownAnswers(int id, uint expected)
    {
        var algorithm = (Crc32Algorithm)id;
        Assert.Equal(expected, Crc32LittleEndian.Compute(algorithm, Check));
        Assert.Equal(expected, Crc32LittleEndian.Compute(algorithm, Check, 0, Check.Length));
    }

    [Fact]
    public void Crc32CEdgeCases()
    {
        Assert.Equal(0u, Crc32LittleEndian.Compute(Crc32Algorithm.Castagnoli, ReadOnlySpan<byte>.Empty));
        Assert.Equal(0x8A9136AAu, Crc32LittleEndian.Compute(Crc32Algorithm.Castagnoli, new byte[32]));
    }

    [Theory]
    [InlineData((int)Crc64Algorithm.Nvme, 0xAE8B14860A799888UL)]
    [InlineData((int)Crc64Algorithm.Ecma182, 0x995DC9BBDF1939FAUL)]
    [InlineData((int)Crc64Algorithm.Iso, 0xB90956C775A41001UL)]
    public void Crc64LittleEndianKnownAnswers(int id, ulong expected)
    {
        var algorithm = (Crc64Algorithm)id;
        Assert.Equal(expected, Crc64LittleEndian.Compute(algorithm, Check));
        Assert.Equal(expected, Crc64LittleEndian.Compute(algorithm, Check, 0, Check.Length));
        Assert.Equal(0UL, Crc64LittleEndian.Compute(algorithm, ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Crc64BigEndianEcmaIsCrc64We()
    {
        Assert.Equal(0x62EC59E3F1A4F00AUL, Crc64BigEndian.Compute(Crc64Algorithm.Ecma182, Check));
        Assert.Equal(0x62EC59E3F1A4F00AUL, Crc64BigEndian.Compute(Crc64Algorithm.Ecma182, Check, 0, Check.Length));
    }

    [Theory]
    [InlineData((int)Crc64Algorithm.Nvme)]
    [InlineData((int)Crc64Algorithm.Ecma182)]
    [InlineData((int)Crc64Algorithm.Iso)]
    public void Crc64ContinuesAcrossChunks(int id)
    {
        var algorithm = (Crc64Algorithm)id;
        var little = new Crc64LittleEndian(algorithm);
        little.Process(Check, 0, 4);
        little.Process(Check.AsSpan(4));
        Assert.Equal(Crc64LittleEndian.Compute(algorithm, Check), little.Value);

        var big = new Crc64BigEndian(algorithm);
        big.Process(Check, 0, 4);
        big.Process(Check.AsSpan(4));
        Assert.Equal(Crc64BigEndian.Compute(algorithm, Check), big.Value);
    }

    [Fact]
    public void Crc64FromAnotherInitialValue()
    {
        // Windows Data Deduplication's records: the NVMe polynomial started from 0, result inverted.
        var fromZero = Crc64LittleEndian.Compute(Crc64Algorithm.Nvme, 0, Check);
        Assert.NotEqual(Crc64LittleEndian.Compute(Crc64Algorithm.Nvme, Check), fromZero);
        Assert.Equal(Crc64LittleEndian.Compute(Crc64Algorithm.Nvme, Check), Crc64LittleEndian.Compute(Crc64Algorithm.Nvme, ulong.MaxValue, Check));

        var instance = new Crc64LittleEndian(Crc64Algorithm.Nvme, 0);
        instance.Process(Check);
        Assert.Equal(fromZero, instance.Value);
    }
}