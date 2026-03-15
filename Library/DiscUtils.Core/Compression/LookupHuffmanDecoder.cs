using System;
using System.Runtime.CompilerServices;

namespace DiscUtils.Compression;

internal ref struct LookupHuffmanDecoder
{
    private readonly int _symbolCount;
    private Span<byte> _lengths;
    private Span<ushort> _table;
    private int _numBits;

    public LookupHuffmanDecoder(
        int symbolCount,
        Span<byte> lengths,
        Span<ushort> table)
    {
        _symbolCount = symbolCount;
        _lengths = lengths.Slice(0, symbolCount);
        _table = table;
        _numBits = 0;
    }

    public bool Build(ReadOnlySpan<byte> codeLengths)
    {
        if (codeLengths.Length < _symbolCount)
        {
            return false;
        }

        codeLengths.Slice(0, _symbolCount).CopyTo(_lengths);

        int maxLength = 0;
        for (int i = 0; i < _symbolCount; i++)
        {
            int len = _lengths[i];
            if (len > maxLength)
            {
                maxLength = len;
            }
        }

        _numBits = maxLength;

        if (_numBits > 16)
        {
            return false;
        }

        if (_numBits == 0)
        {
            return true;
        }

        int tableSize = 1 << _numBits;
        if (_table.Length < tableSize)
        {
            return false;
        }

        var table = _table.Slice(0, tableSize);
        table.Fill(ushort.MaxValue);

        int position = 0;

        for (int bitLength = 1; bitLength <= _numBits; bitLength++)
        {
            for (ushort symbol = 0; symbol < _symbolCount; symbol++)
            {
                if (_lengths[symbol] == bitLength)
                {
                    int numToFill = 1 << (_numBits - bitLength);
                    if (position > tableSize - numToFill)
                    {
                        return false;
                    }

                    table.Slice(position, numToFill).Fill(symbol);
                    position += numToFill;
                }
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Decode(scoped ref LzxBitReader reader)
    {
        if (_numBits == 0)
        {
            return -1;
        }

        if (!reader.TryPeekBits(_numBits, out uint peek))
        {
            return -1;
        }

        ushort symbol = _table[(int)peek];
        if (symbol == ushort.MaxValue)
        {
            return -1;
        }

        int len = _lengths[symbol];
        if (len <= 0)
        {
            return -1;
        }

        if (!reader.TryConsumeBits(len))
        {
            return -1;
        }

        return symbol;
    }
}
