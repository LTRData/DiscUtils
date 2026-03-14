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

    public LzxBitReader(ReadOnlySpan<byte> source)
    {
        _source = source;
        _rawPos = 0;
        _bitBuffer = 0;
        _bitsAvailable = 0;
    }

    public int BytesConsumed => _rawPos;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool EnsureBufferFilled()
    {
        // Match the old style bit-reader behavior:
        // pull another 16 bits whenever fewer than 16 remain.
        if (_bitsAvailable < 16)
        {
            if ((uint)(_rawPos + 1) >= (uint)_source.Length)
            {
                return false;
            }

            ushort word = BinaryPrimitives.ReadUInt16LittleEndian(_source.Slice(_rawPos, 2));
            _rawPos += 2;
            _bitBuffer = (_bitBuffer << 16) | word;
            _bitsAvailable += 16;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPeekBits(int count, out uint value)
    {
        value = 0;

        if ((uint)count > 16u)
        {
            return false;
        }

        if (!EnsureBufferFilled())
        {
            return false;
        }

        if (_bitsAvailable < count)
        {
            return false;
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

        if (!EnsureBufferFilled())
        {
            return false;
        }

        if (_bitsAvailable < count)
        {
            return false;
        }

        _bitsAvailable -= count;

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

        if (!EnsureBufferFilled())
        {
            return false;
        }

        if (_bitsAvailable < count)
        {
            return false;
        }

        _bitsAvailable -= count;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AlignTo16Bits()
    {
        int discard = _bitsAvailable & 0x0F;
        return TryConsumeBits(discard);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadRawByte(out byte value)
    {
        if ((uint)_rawPos >= (uint)_source.Length)
        {
            value = 0;
            return false;
        }

        value = _source[_rawPos++];
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadRawUInt32(out uint value)
    {
        if ((uint)(_rawPos + 3) >= (uint)_source.Length)
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(_source.Slice(_rawPos, 4));
        _rawPos += 4;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadRawBytes(scoped Span<byte> destination)
    {
        if ((uint)_rawPos > (uint)_source.Length || destination.Length > _source.Length - _rawPos)
        {
            return false;
        }

        _source.Slice(_rawPos, destination.Length).CopyTo(destination);
        _rawPos += destination.Length;
        return true;
    }
}
