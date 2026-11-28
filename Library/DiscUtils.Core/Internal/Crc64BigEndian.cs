using System;
using System.Collections.Immutable;

namespace DiscUtils.Internal;

/// <summary>
/// Calculates non-reflected (most significant bit first) CRC-64 of buffers: initial value all ones, result inverted
/// (with <see cref="Crc64Algorithm.Ecma182"/> this is CRC-64/WE).
/// </summary>
internal sealed class Crc64BigEndian : Crc64
{
    private static readonly ImmutableArray<ImmutableArray<ulong>> Tables;

    static Crc64BigEndian()
    {
        var tables = new ImmutableArray<ulong>[3];

        tables[(int)Crc64Algorithm.Nvme] = CalcTable(0xAD93D23594C93659);
        tables[(int)Crc64Algorithm.Ecma182] = CalcTable(0x42F0E1EBA9EA3693);
        tables[(int)Crc64Algorithm.Iso] = CalcTable(0x000000000000001B);

        Tables = tables.ToImmutableArray();
    }

    public Crc64BigEndian(Crc64Algorithm algorithm)
        : base(Tables[(int)algorithm]) { }

    public static ulong Compute(Crc64Algorithm algorithm, byte[] buffer, int offset, int count)
    {
        return Process(Tables[(int)algorithm], 0xFFFFFFFFFFFFFFFF, buffer.AsSpan(offset, count)) ^ 0xFFFFFFFFFFFFFFFF;
    }

    public static ulong Compute(Crc64Algorithm algorithm, ReadOnlySpan<byte> buffer)
    {
        return Process(Tables[(int)algorithm], 0xFFFFFFFFFFFFFFFF, buffer) ^ 0xFFFFFFFFFFFFFFFF;
    }

    public override void Process(ReadOnlySpan<byte> buffer)
    {
        _value = Process(Table, _value, buffer);
    }

    private static ImmutableArray<ulong> CalcTable(ulong polynomial)
    {
        Span<ulong> table = stackalloc ulong[256];

        for (ulong i = 0; i < 256; ++i)
        {
            var crc = i << 56;

            for (var j = 8; j > 0; --j)
            {
                if ((crc & 0x8000000000000000) != 0)
                {
                    crc = (crc << 1) ^ polynomial;
                }
                else
                {
                    crc <<= 1;
                }
            }

            table[(int)i] = crc;
        }

        return table.ToImmutableArray();
    }

    private static ulong Process(ImmutableArray<ulong> table, ulong accumulator, ReadOnlySpan<byte> buffer)
    {
        var value = accumulator;

        for (var i = 0; i < buffer.Length; ++i)
        {
            value = (value << 8) ^ table[(int)(((value >> 56) ^ buffer[i]) & 0xFF)];
        }

        return value;
    }
}
