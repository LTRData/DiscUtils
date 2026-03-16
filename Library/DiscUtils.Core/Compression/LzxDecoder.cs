using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace DiscUtils.Compression;

internal ref struct LzxDecoder
{
    private static readonly uint[] s_positionSlots;
    private static readonly uint[] s_extraBits;

    private readonly int _windowBits;
    private readonly int _windowSize;
    private readonly int _E8FixupMaxSize;
    private readonly int _numPositionSlots;

    private readonly LzxWorkspace _workspace;

    private Span<byte> _window;
    private int _windowPos;

    private uint _r0;
    private uint _r1;
    private uint _r2;

    private Span<byte> _mainLengths;
    private Span<byte> _lengthLengths;
    private Span<byte> _alignedLengths;
    private Span<byte> _preTreeLengths;

    static LzxDecoder()
    {
        var positionSlots = new uint[50];
        var extraBits = new uint[50];

        uint numBits = 0;
        positionSlots[1] = 1;

        for (int i = 2; i < 50; i += 2)
        {
            extraBits[i] = numBits;
            extraBits[i + 1] = numBits;
            positionSlots[i] = positionSlots[i - 1] + (uint)(1 << (int)extraBits[i - 1]);
            positionSlots[i + 1] = positionSlots[i] + (uint)(1 << (int)numBits);

            if (numBits < 17)
            {
                numBits++;
            }
        }

        s_positionSlots = positionSlots;
        s_extraBits = extraBits;
    }

    public LzxDecoder(int windowBits, int e8FixupMaxSize, LzxWorkspace workspace)
    {
        _windowBits = windowBits;
        _windowSize = 1 << windowBits;
        _E8FixupMaxSize = e8FixupMaxSize;
        _numPositionSlots = _windowBits * 2;

        _workspace = workspace;

        _window = workspace.Window.Slice(0, _windowSize);
        _mainLengths = workspace.MainLengths.Slice(0, 256 + 8 * _numPositionSlots);
        _lengthLengths = workspace.LengthLengths.Slice(0, 249);
        _alignedLengths = workspace.AlignedLengths.Slice(0, 8);
        _preTreeLengths = workspace.PreTreeLengths.Slice(0, 20);

        _window.Clear();
        _mainLengths.Clear();
        _lengthLengths.Clear();
        _alignedLengths.Clear();
        _preTreeLengths.Clear();

        _windowPos = 0;
        _r0 = 1;
        _r1 = 1;
        _r2 = 1;
    }

    public bool TryDecompress(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        out int bytesConsumed,
        out int bytesWritten)
    {
        bytesConsumed = 0;
        bytesWritten = 0;

        var reader = new LzxBitReader(source);
        int dstPos = 0;

        if (!reader.TryReadBits(3, out uint blockTypeValue))
        {
            return false;
        }

        var blockType = (BlockType)blockTypeValue;

        while (blockType != BlockType.None && dstPos < destination.Length)
        {
            if (!TryReadBlockSize(ref reader, out int blockSize))
            {
                bytesConsumed = reader.BytesConsumed;
                bytesWritten = dstPos;
                return false;
            }

            switch (blockType)
            {
                case BlockType.Uncompressed:
                    if (!DecodeUncompressedBlock(ref reader, destination, ref dstPos, blockSize))
                    {
                        bytesConsumed = reader.BytesConsumed;
                        bytesWritten = dstPos;
                        return false;
                    }
                    break;

                case BlockType.Verbatim:
                case BlockType.AlignedOffset:
                    if (!DecodeCompressedBlock(ref reader, blockType, destination, ref dstPos, blockSize))
                    {
                        bytesConsumed = reader.BytesConsumed;
                        bytesWritten = dstPos;
                        return false;
                    }
                    break;

                default:
                    bytesConsumed = reader.BytesConsumed;
                    bytesWritten = dstPos;
                    return false;
            }

            if (!reader.TryReadBits(3, out blockTypeValue))
            {
                bytesConsumed = reader.BytesConsumed;
                bytesWritten = dstPos;
                return false;
            }

            blockType = (BlockType)blockTypeValue;
        }

        if (_E8FixupMaxSize > 0)
        {
            ApplyE8Fixup(destination.Slice(0, dstPos), _E8FixupMaxSize);
        }

        bytesConsumed = reader.BytesConsumed;
        bytesWritten = dstPos;
        return true;
    }

    private static bool TryReadBlockSize(scoped ref LzxBitReader reader, out int blockSize)
    {
        blockSize = 0;

        if (!reader.TryReadBits(1, out uint hi))
        {
            return false;
        }

        if (hi == 1)
        {
            blockSize = 1 << 15;
            return true;
        }

        if (!reader.TryReadBits(16, out uint lo))
        {
            return false;
        }

        blockSize = (int)lo;
        return true;
    }

    private bool DecodeUncompressedBlock(
        scoped ref LzxBitReader reader,
        Span<byte> output,
        ref int dstPos,
        int blockSize)
    {
        if (!reader.AlignTo16Bits())
        {
            return false;
        }

        if (!reader.TryReadRawUInt32(out _r0) ||
            !reader.TryReadRawUInt32(out _r1) ||
            !reader.TryReadRawUInt32(out _r2))
        {
            return false;
        }

        int remainingOutput = output.Length - dstPos;
        int bytesToStore = Math.Min(blockSize, remainingOutput);

        if (!reader.TryReadRawBytes(output.Slice(dstPos, bytesToStore)))
        {
            return false;
        }

        for (int i = 0; i < bytesToStore; i++)
        {
            WriteWindowByte(output[dstPos + i]);
        }

        dstPos += bytesToStore;

        int skipped = blockSize - bytesToStore;
        if (skipped > 0)
        {
            Span<byte> scratch = stackalloc byte[256];

            while (skipped > 0)
            {
                int chunk = Math.Min(skipped, scratch.Length);
                if (!reader.TryReadRawBytes(scratch.Slice(0, chunk)))
                {
                    return false;
                }

                for (int i = 0; i < chunk; i++)
                {
                    WriteWindowByte(scratch[i]);
                }

                skipped -= chunk;
            }
        }

        if ((blockSize & 1) != 0)
        {
            if (!reader.TryReadRawByte(out _))
            {
                return false;
            }
        }

        return true;
    }

    private bool DecodeCompressedBlock(
        scoped ref LzxBitReader reader,
        BlockType blockType,
        Span<byte> output,
        ref int dstPos,
        int blockSize)
    {
        LookupHuffmanDecoder alignedTree = default;
        bool haveAlignedTree = false;

        if (blockType == BlockType.AlignedOffset)
        {
            if (!ReadAlignedTree(ref reader, out alignedTree))
            {
                return false;
            }

            haveAlignedTree = true;
        }

        if (!ReadMainTree(ref reader, out var mainTree))
        {
            return false;
        }

        if (!ReadLengthTree(ref reader, out var lengthTree))
        {
            return false;
        }

        int blockRemaining = blockSize;

        while (blockRemaining > 0)
        {
            int symbol = mainTree.Decode(ref reader);
            if (symbol < 0)
            {
                return false;
            }

            if (symbol < 256)
            {
                WriteLiteral(output, ref dstPos, (byte)symbol);
                blockRemaining--;
                continue;
            }

            int footer = symbol - 256;
            int lengthHeader = footer & 7;
            int positionSlot = footer >> 3;

            int matchLength = lengthHeader + 2;
            if (lengthHeader == 7)
            {
                int extraLen = lengthTree.Decode(ref reader);
                if (extraLen < 0)
                {
                    return false;
                }

                matchLength += extraLen;
            }

            if (!TryDecodeMatchOffset(
                ref reader,
                blockType,
                positionSlot,
                haveAlignedTree,
                ref alignedTree,
                out uint matchOffset))
            {
                return false;
            }

            if (!CopyMatchFromWindow(output, ref dstPos, matchOffset, matchLength, ref blockRemaining))
            {
                return false;
            }
        }

        return true;
    }

    private bool ReadMainTree(scoped ref LzxBitReader reader, out LookupHuffmanDecoder mainTree)
    {
        var preTree = CreatePreTreeDecoder();
        if (!ReadFixedTree(ref reader, _preTreeLengths, 20, 4, ref preTree))
        {
            mainTree = default;
            return false;
        }

        if (!ReadLengths(ref reader, ref preTree, _mainLengths, 0, 256))
        {
            mainTree = default;
            return false;
        }

        preTree = CreatePreTreeDecoder();
        if (!ReadFixedTree(ref reader, _preTreeLengths, 20, 4, ref preTree))
        {
            mainTree = default;
            return false;
        }

        if (!ReadLengths(ref reader, ref preTree, _mainLengths, 256, 8 * _numPositionSlots))
        {
            mainTree = default;
            return false;
        }

        mainTree = CreateMainDecoder();
        return mainTree.Build(_mainLengths);
    }

    private bool ReadLengthTree(scoped ref LzxBitReader reader, out LookupHuffmanDecoder lengthTree)
    {
        var preTree = CreatePreTreeDecoder();
        if (!ReadFixedTree(ref reader, _preTreeLengths, 20, 4, ref preTree))
        {
            lengthTree = default;
            return false;
        }

        if (!ReadLengths(ref reader, ref preTree, _lengthLengths, 0, 249))
        {
            lengthTree = default;
            return false;
        }

        lengthTree = CreateLengthDecoder();
        return lengthTree.Build(_lengthLengths);
    }

    private bool ReadAlignedTree(scoped ref LzxBitReader reader, out LookupHuffmanDecoder alignedTree)
    {
        var decoder = CreateAlignedDecoder();

        if (!ReadFixedTree(ref reader, _alignedLengths, 8, 3, ref decoder))
        {
            alignedTree = default;
            return false;
        }

        alignedTree = decoder;

        return true;
    }

    private static bool ReadFixedTree(
        scoped ref LzxBitReader reader,
        Span<byte> codeLengths,
        int symbolCount,
        int bitsPerLength,
        scoped ref LookupHuffmanDecoder decoder)
    {
        for (int i = 0; i < symbolCount; i++)
        {
            if (!reader.TryReadBits(bitsPerLength, out uint value))
            {
                return false;
            }

            codeLengths[i] = (byte)value;
        }

        return decoder.Build(codeLengths.Slice(0, symbolCount));
    }

    private static bool ReadLengths(
        scoped ref LzxBitReader reader,
        scoped ref LookupHuffmanDecoder preTree,
        Span<byte> lengths,
        int offset,
        int count)
    {
        int i = 0;

        while (i < count)
        {
            int value = preTree.Decode(ref reader);
            if (value < 0)
            {
                return false;
            }

            if (value == 17)
            {
                if (!reader.TryReadBits(4, out uint n))
                {
                    return false;
                }

                int numZeros = 4 + (int)n;
                for (int j = 0; j < numZeros && i < count; j++)
                {
                    lengths[offset + i++] = 0;
                }
            }
            else if (value == 18)
            {
                if (!reader.TryReadBits(5, out uint n))
                {
                    return false;
                }

                int numZeros = 20 + (int)n;
                for (int j = 0; j < numZeros && i < count; j++)
                {
                    lengths[offset + i++] = 0;
                }
            }
            else if (value == 19)
            {
                if (!reader.TryReadBits(1, out uint same))
                {
                    return false;
                }

                int extra = preTree.Decode(ref reader);
                if ((uint)extra > 16u)
                {
                    return false;
                }

                byte symbol = (byte)((17 + lengths[offset + i] - extra) % 17);
                int reps = 4 + (int)same;

                for (int j = 0; j < reps && i < count; j++)
                {
                    lengths[offset + i++] = symbol;
                }
            }
            else
            {
                lengths[offset + i] = (byte)((17 + lengths[offset + i] - value) % 17);
                i++;
            }
        }

        return true;
    }

    private bool TryDecodeMatchOffset(
        scoped ref LzxBitReader reader,
        BlockType blockType,
        int positionSlot,
        bool haveAlignedTree,
        scoped ref LookupHuffmanDecoder alignedTree,
        out uint matchOffset)
    {
        matchOffset = 0;

        if (positionSlot == 0)
        {
            matchOffset = _r0;
            return true;
        }

        if (positionSlot == 1)
        {
            matchOffset = _r1;
            _r1 = _r0;
            _r0 = matchOffset;
            return true;
        }

        if (positionSlot == 2)
        {
            matchOffset = _r2;
            _r2 = _r0;
            _r0 = matchOffset;
            return true;
        }

        int extra = (int)s_extraBits[positionSlot];
        uint formattedOffset;

        if (blockType == BlockType.AlignedOffset)
        {
            if (!haveAlignedTree)
            {
                return false;
            }

            uint verbatimBits = 0;
            uint alignedBits = 0;

            if (extra >= 3)
            {
                if (!reader.TryReadBits(extra - 3, out uint highBits))
                {
                    return false;
                }

                verbatimBits = highBits << 3;

                int alignedSymbol = alignedTree.Decode(ref reader);
                if (alignedSymbol < 0)
                {
                    return false;
                }

                alignedBits = (uint)alignedSymbol;
            }
            else if (extra > 0)
            {
                if (!reader.TryReadBits(extra, out verbatimBits))
                {
                    return false;
                }
            }

            formattedOffset = s_positionSlots[positionSlot] + verbatimBits + alignedBits;
        }
        else
        {
            uint verbatimBits = 0;
            if (extra > 0)
            {
                if (!reader.TryReadBits(extra, out verbatimBits))
                {
                    return false;
                }
            }

            formattedOffset = s_positionSlots[positionSlot] + verbatimBits;
        }

        matchOffset = formattedOffset - 2;

        _r2 = _r1;
        _r1 = _r0;
        _r0 = matchOffset;
        return true;
    }

    private bool CopyMatchFromWindow(
        Span<byte> output,
        ref int dstPos,
        uint matchOffset,
        int matchLength,
        ref int blockRemaining)
    {
        if (matchOffset == 0 || matchOffset > (uint)_windowSize)
        {
            return false;
        }

        if (matchLength < 0 || matchLength > blockRemaining)
        {
            return false;
        }

        int src = _windowPos - (int)matchOffset;
        if (src < 0)
        {
            src += _windowSize;
        }

        while (matchLength > 0)
        {
            // How much can we read contiguously from current source position
            // before wrapping the circular window?
            int srcRun = _windowSize - src;

            // How much can we write contiguously to current window position
            // before wrapping the circular window?
            int dstRun = _windowSize - _windowPos;

            int chunk = Math.Min(matchLength, Math.Min(srcRun, dstRun));

            // If source and destination are safely separated inside the current
            // contiguous chunk, we can copy the whole chunk with Span.CopyTo.
            // Otherwise fall back to byte-serial copying to preserve overlap behavior.
            if (chunk > 0 && (src + chunk <= _windowPos || _windowPos + chunk <= src))
            {
                var srcSlice = _window.Slice(src, chunk);
                var dstSlice = _window.Slice(_windowPos, chunk);

                srcSlice.CopyTo(dstSlice);

                int outChunk = Math.Min(chunk, output.Length - dstPos);
                if (outChunk > 0)
                {
                    dstSlice.Slice(0, outChunk).CopyTo(output.Slice(dstPos, outChunk));
                }

                dstPos += chunk;
                blockRemaining -= chunk;
                matchLength -= chunk;

                _windowPos += chunk;
                if (_windowPos == _windowSize)
                {
                    _windowPos = 0;
                }

                src += chunk;
                if (src == _windowSize)
                {
                    src = 0;
                }
            }
            else
            {
                byte value = _window[src];

                if ((uint)dstPos < (uint)output.Length)
                {
                    output[dstPos] = value;
                }

                _window[_windowPos] = value;

                dstPos++;
                blockRemaining--;
                matchLength--;

                src++;
                if (src == _windowSize)
                {
                    src = 0;
                }

                _windowPos++;
                if (_windowPos == _windowSize)
                {
                    _windowPos = 0;
                }
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteLiteral(Span<byte> output, ref int dstPos, byte value)
    {
        if ((uint)dstPos < (uint)output.Length)
        {
            output[dstPos] = value;
        }

        dstPos++;
        WriteWindowByte(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LookupHuffmanDecoder CreateMainDecoder()
        => new(_mainLengths.Length, _mainLengths, _workspace.MainTable);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LookupHuffmanDecoder CreateLengthDecoder()
        => new(249, _lengthLengths, _workspace.LengthTable);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LookupHuffmanDecoder CreateAlignedDecoder()
        => new(8, _alignedLengths, _workspace.AlignedTable);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LookupHuffmanDecoder CreatePreTreeDecoder()
        => new(20, _preTreeLengths, _workspace.PreTreeTable);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteWindowByte(byte value)
    {
        _window[_windowPos] = value;
        _windowPos++;
        if (_windowPos == _windowSize)
        {
            _windowPos = 0;
        }
    }

    private static void ApplyE8Fixup(Span<byte> buffer, int fileSize)
    {
        int i = 0;
        while (i < buffer.Length - 10)
        {
            if (buffer[i] == 0xE8)
            {
                int absoluteValue = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(i + 1, 4));
                if (absoluteValue >= -i && absoluteValue < fileSize)
                {
                    int offsetValue = absoluteValue >= 0
                        ? absoluteValue - i
                        : absoluteValue + fileSize;

                    BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(i + 1, 4), offsetValue);
                }

                i += 4;
            }

            i++;
        }
    }

    private enum BlockType
    {
        None = 0,
        Verbatim = 1,
        AlignedOffset = 2,
        Uncompressed = 3
    }
}