using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace DiscUtils.Compression;

internal ref struct LzxBitReader
{
    private readonly ReadOnlySpan<byte> _source;
    private int _rawPos;
    private uint _bitBuffer;
    private int _bitsAvailable;
    private long _positionBits;

    public LzxBitReader(ReadOnlySpan<byte> source)
    {
        _source = source;
        _rawPos = 0;
        _bitBuffer = 0;
        _bitsAvailable = 0;
        _positionBits = 0;
    }

    public int BytesConsumed => _rawPos;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Need(int count)
    {
        while (_bitsAvailable < count)
        {
            byte lo = 0;
            byte hi = 0;

            if ((uint)_rawPos < (uint)_source.Length)
            {
                lo = _source[_rawPos++];
            }

            if ((uint)_rawPos < (uint)_source.Length)
            {
                hi = _source[_rawPos++];
            }

            _bitBuffer = (_bitBuffer << 16) | (uint)(lo | (hi << 8));
            _bitsAvailable += 16;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPeekBits(int count, out uint value)
    {
        value = 0;

        if ((uint)count > 16u)
        {
            return false;
        }

        if (_bitsAvailable < count)
        {
            Need(count);
        }

        if (count == 0)
        {
            return true;
        }

        uint mask = (1u << count) - 1u;
        value = (_bitBuffer >> (_bitsAvailable - count)) & mask;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadBits(int count, out uint value)
    {
        value = 0;

        if ((uint)count > 16u)
        {
            return false;
        }

        if (_bitsAvailable < count)
        {
            Need(count);
        }

        _bitsAvailable -= count;
        _positionBits += count;

        if (count == 0)
        {
            return true;
        }

        uint mask = (1u << count) - 1u;
        value = (_bitBuffer >> _bitsAvailable) & mask;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryConsumeBits(int count)
    {
        if ((uint)count > 16u)
        {
            return false;
        }

        if (_bitsAvailable < count)
        {
            Need(count);
        }

        _bitsAvailable -= count;
        _positionBits += count;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AlignTo16Bits()
    {
        // Match legacy Align(16): consumes 1..16 bits, never 0.
        int offset = (int)(_positionBits % 16);
        int consume = 16 - offset;
        return TryConsumeBits(consume);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadRawByte(out byte value)
    {
        if ((_positionBits & 7) != 0)
        {
            value = 0;
            return false;
        }

        if ((uint)_rawPos >= (uint)_source.Length)
        {
            value = 0;
            return false;
        }

        value = _source[_rawPos++];
        _positionBits += 8;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadRawUInt32(out uint value)
    {
        value = 0;

        if ((_positionBits & 7) != 0)
        {
            return false;
        }

        if ((uint)(_rawPos + 3) >= (uint)_source.Length)
        {
            return false;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(_source.Slice(_rawPos, 4));
        _rawPos += 4;
        _positionBits += 32;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadRawBytes(scoped Span<byte> destination)
    {
        if ((_positionBits & 7) != 0)
        {
            return false;
        }

        if ((uint)_rawPos > (uint)_source.Length || destination.Length > _source.Length - _rawPos)
        {
            return false;
        }

        _source.Slice(_rawPos, destination.Length).CopyTo(destination);
        _rawPos += destination.Length;
        _positionBits += destination.Length * 8L;
        return true;
    }
}
