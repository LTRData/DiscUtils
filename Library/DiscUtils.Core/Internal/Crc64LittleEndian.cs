using System;
using System.Collections.Immutable;

namespace DiscUtils.Internal;

/// <summary>
/// Calculates reflected (least significant bit first) CRC-64 of buffers: initial value all ones and result inverted, unless another
/// initial value is given.
/// </summary>
internal sealed class Crc64LittleEndian : Crc64
{
    private static readonly ImmutableArray<ImmutableArray<ulong>> Tables;

    static Crc64LittleEndian()
    {
        var tables = new ImmutableArray<ulong>[3];

        tables[(int)Crc64Algorithm.Nvme] = CalcTable(0x9A6C9329AC4BC9B5);
        tables[(int)Crc64Algorithm.Ecma182] = CalcTable(0xC96C5795D7870F42);
        tables[(int)Crc64Algorithm.Iso] = CalcTable(0xD800000000000000);

        Tables = tables.ToImmutableArray();
    }

    public Crc64LittleEndian(Crc64Algorithm algorithm)
        : base(Tables[(int)algorithm]) { }

    /// <summary>Starts from <paramref name="initial"/> instead of all ones (Windows Data Deduplication's records start from 0).</summary>
    public Crc64LittleEndian(Crc64Algorithm algorithm, ulong initial)
        : base(Tables[(int)algorithm], initial) { }

    public static ulong Compute(Crc64Algorithm algorithm, byte[] buffer, int offset, int count)
    {
        return Process(Tables[(int)algorithm], 0xFFFFFFFFFFFFFFFF, buffer.AsSpan(offset, count)) ^ 0xFFFFFFFFFFFFFFFF;
    }

    public static ulong Compute(Crc64Algorithm algorithm, ReadOnlySpan<byte> buffer)
    {
        return Process(Tables[(int)algorithm], 0xFFFFFFFFFFFFFFFF, buffer) ^ 0xFFFFFFFFFFFFFFFF;
    }

    /// <summary>Starts from <paramref name="initial"/> instead of all ones; the result is still inverted.</summary>
    public static ulong Compute(Crc64Algorithm algorithm, ulong initial, ReadOnlySpan<byte> buffer)
    {
        return Process(Tables[(int)algorithm], initial, buffer) ^ 0xFFFFFFFFFFFFFFFF;
    }

    public override void Process(ReadOnlySpan<byte> buffer)
    {
        _value = Process(Table, _value, buffer);
    }

    private static ImmutableArray<ulong> CalcTable(ulong polynomial)
    {
        Span<ulong> table = stackalloc ulong[256];

        for (ulong i = 0; i <= 255; ++i)
        {
            var crc = i;

            for (var j = 8; j > 0; --j)
            {
                if ((crc & 1) != 0)
                {
                    crc = (crc >> 1) ^ polynomial;
                }
                else
                {
                    crc >>= 1;
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
            value = (value >> 8) ^ table[(int)((value ^ buffer[i]) & 0xFF)];
        }

        return value;
    }
}
