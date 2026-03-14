using System;
using System.Runtime.CompilerServices;

namespace DiscUtils.Compression;

internal ref struct CanonicalHuffmanDecoder
{
    private readonly int _symbolCount;
    private readonly int _maxCodeLength;
    private readonly int _fastBits;

    private Span<ushort> _sortedSymbols;
    private Span<ushort> _lengthCounts;
    private Span<int> _firstCode;
    private Span<int> _firstSymbol;
    private Span<int> _nextSymbol;
    private Span<ushort> _fastSymbols;
    private Span<byte> _fastLengths;

    public CanonicalHuffmanDecoder(
        int symbolCount,
        int maxCodeLength,
        int fastBits,
        Span<ushort> sortedSymbols,
        Span<ushort> lengthCounts,
        Span<int> firstCode,
        Span<int> firstSymbol,
        Span<int> nextSymbol,
        Span<ushort> fastSymbols,
        Span<byte> fastLengths)
    {
        _symbolCount = symbolCount;
        _maxCodeLength = maxCodeLength;
        _fastBits = fastBits;
        _sortedSymbols = sortedSymbols.Slice(0, symbolCount);
        _lengthCounts = lengthCounts.Slice(0, maxCodeLength + 1);
        _firstCode = firstCode.Slice(0, maxCodeLength + 1);
        _firstSymbol = firstSymbol.Slice(0, maxCodeLength + 1);
        _nextSymbol = nextSymbol.Slice(0, maxCodeLength + 1);
        _fastSymbols = fastSymbols.Slice(0, 1 << fastBits);
        _fastLengths = fastLengths.Slice(0, 1 << fastBits);
    }

    public bool Build(ReadOnlySpan<byte> codeLengths)
    {
        if (codeLengths.Length < _symbolCount)
        {
            return false;
        }

        _sortedSymbols.Clear();
        _lengthCounts.Clear();
        _firstCode.Clear();
        _firstSymbol.Clear();
        _nextSymbol.Clear();
        _fastSymbols.Clear();
        _fastLengths.Clear();

        for (int symbol = 0; symbol < _symbolCount; symbol++)
        {
            int len = codeLengths[symbol];
            if ((uint)len > (uint)_maxCodeLength)
            {
                return false;
            }

            if (len != 0)
            {
                _lengthCounts[len]++;
            }
        }

        int runningSymbolIndex = 0;
        int code = 0;

        for (int len = 1; len <= _maxCodeLength; len++)
        {
            code <<= 1;
            _firstCode[len] = code;
            _firstSymbol[len] = runningSymbolIndex;
            _nextSymbol[len] = runningSymbolIndex;

            runningSymbolIndex += _lengthCounts[len];
            code += _lengthCounts[len];

            if (code > (1 << len))
            {
                return false;
            }
        }

        for (int symbol = 0; symbol < _symbolCount; symbol++)
        {
            int len = codeLengths[symbol];
            if (len == 0)
            {
                continue;
            }

            _sortedSymbols[_nextSymbol[len]++] = (ushort)symbol;
        }

        for (int len = 1; len <= _fastBits; len++)
        {
            int count = _lengthCounts[len];
            if (count == 0)
            {
                continue;
            }

            int baseCode = _firstCode[len];
            int symbolBase = _firstSymbol[len];
            int fill = 1 << (_fastBits - len);

            for (int i = 0; i < count; i++)
            {
                int codeValue = baseCode + i;
                int start = codeValue << (_fastBits - len);
                ushort symbol = _sortedSymbols[symbolBase + i];

                for (int j = 0; j < fill; j++)
                {
                    _fastSymbols[start + j] = symbol;
                    _fastLengths[start + j] = (byte)len;
                }
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Decode(scoped ref LzxBitReader reader)
    {
        if (!reader.TryPeekBits(1, out _))
        {
            return -1;
        }

        if (reader.TryPeekBits(_fastBits, out uint fastPeek))
        {
            byte len = _fastLengths[(int)fastPeek];
            if (len != 0)
            {
                if (!reader.TryConsumeBits(len))
                {
                    return -1;
                }

                return _fastSymbols[(int)fastPeek];
            }
        }

        for (int len = 1; len <= _maxCodeLength; len++)
        {
            if (!reader.TryPeekBits(len, out uint peek))
            {
                return -1;
            }

            int code = (int)peek;
            int delta = code - _firstCode[len];
            int count = _lengthCounts[len];

            if ((uint)delta < (uint)count)
            {
                if (!reader.TryConsumeBits(len))
                {
                    return -1;
                }

                return _sortedSymbols[_firstSymbol[len] + delta];
            }
        }

        return -1;
    }
}
