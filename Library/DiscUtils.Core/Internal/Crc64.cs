using System;
using System.Collections.Immutable;

namespace DiscUtils.Internal;

internal abstract class Crc64
{
    protected readonly ImmutableArray<ulong> Table;
    protected ulong _value;

    protected Crc64(ImmutableArray<ulong> table)
        : this(table, 0xFFFFFFFFFFFFFFFF) { }

    protected Crc64(ImmutableArray<ulong> table, ulong initial)
    {
        Table = table;
        _value = initial;
    }

    public ulong Value => _value ^ 0xFFFFFFFFFFFFFFFF;

    public void Process(byte[] buffer, int offset, int count)
        => Process(buffer.AsSpan(offset, count));

    public abstract void Process(ReadOnlySpan<byte> buffer);
}
