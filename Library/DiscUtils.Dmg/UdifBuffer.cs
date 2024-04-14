//
// Copyright (c) 2008-2011, Kenneth Bell
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Compression;
using DiscUtils.Streams;
using Buffer=DiscUtils.Streams.Buffer;

namespace DiscUtils.Dmg;

internal class UdifBuffer : Buffer
{
    private CompressedRun _activeRun;
    private long _activeRunOffset;

    private byte[] _decompBuffer;
    private readonly ResourceFork _resources;
    private readonly long _sectorCount;
    private readonly Stream _stream;

    public UdifBuffer(Stream stream, ResourceFork resources, long sectorCount)
    {
        _stream = stream;
        _resources = resources;
        _sectorCount = sectorCount;

        Blocks = new List<CompressedBlock>();

        foreach (var resource in _resources.GetAllResources("blkx"))
        {
            Blocks.Add(((BlkxResource)resource).Block);
        }
    }

    public List<CompressedBlock> Blocks { get; }

    public override bool CanRead => true;

    public override bool CanWrite => false;

    public override long Capacity => _sectorCount * Sizes.Sector;

    public override int Read(long pos, byte[] buffer, int offset, int count)
    {
        var totalCopied = 0;
        var currentPos = pos;

        while (totalCopied < count && currentPos < Capacity)
        {
            LoadRun(currentPos);

            var bufferOffset = (int)(currentPos - (_activeRunOffset + _activeRun.SectorStart * Sizes.Sector));
            var toCopy = (int)Math.Min(_activeRun.SectorCount * Sizes.Sector - bufferOffset, count - totalCopied);

            switch (_activeRun.Type)
            {
                case RunType.Zeros:
                    Array.Clear(buffer, offset + totalCopied, toCopy);
                    break;

                case RunType.Raw:
                    _stream.Position = _activeRun.CompOffset + bufferOffset;
                    _stream.ReadExactly(buffer, offset + totalCopied, toCopy);
                    break;

                case RunType.AdcCompressed:
                case RunType.ZlibCompressed:
                case RunType.BZlibCompressed:
                case RunType.LzfseCompressed:
                    System.Buffer.BlockCopy(_decompBuffer, bufferOffset, buffer, offset + totalCopied, toCopy);
                    break;

                default:
                    throw new NotImplementedException($"Reading from run of type {_activeRun.Type}");
            }

            currentPos += toCopy;
            totalCopied += toCopy;
        }

        return totalCopied;
    }

    public override async ValueTask<int> ReadAsync(long pos, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var totalCopied = 0;
        var currentPos = pos;

        while (totalCopied < buffer.Length && currentPos < Capacity)
        {
            await LoadRunAsync(currentPos, cancellationToken).ConfigureAwait(false);

            var bufferOffset = (int)(currentPos - (_activeRunOffset + _activeRun.SectorStart * Sizes.Sector));
            var toCopy = (int)Math.Min(_activeRun.SectorCount * Sizes.Sector - bufferOffset, buffer.Length - totalCopied);

            switch (_activeRun.Type)
            {
                case RunType.Zeros:
                    buffer.Span.Slice(totalCopied, toCopy).Clear();
                    break;

                case RunType.Raw:
                    _stream.Position = _activeRun.CompOffset + bufferOffset;
                    await _stream.ReadExactlyAsync(buffer.Slice(totalCopied, toCopy), cancellationToken).ConfigureAwait(false);
                    break;

                case RunType.AdcCompressed:
                case RunType.ZlibCompressed:
                case RunType.BZlibCompressed:
                case RunType.LzfseCompressed:
                    _decompBuffer.AsMemory(bufferOffset, toCopy).CopyTo(buffer.Slice(totalCopied));
                    break;

                default:
                    throw new NotImplementedException($"Reading from run of type {_activeRun.Type}");
            }

            currentPos += toCopy;
            totalCopied += toCopy;
        }

        return totalCopied;
    }

    public override int Read(long pos, Span<byte> buffer)
    {
        var totalCopied = 0;
        var currentPos = pos;

        while (totalCopied < buffer.Length && currentPos < Capacity)
        {
            LoadRun(currentPos);

            var bufferOffset = (int)(currentPos - (_activeRunOffset + _activeRun.SectorStart * Sizes.Sector));
            var toCopy = (int)Math.Min(_activeRun.SectorCount * Sizes.Sector - bufferOffset, buffer.Length - totalCopied);

            switch (_activeRun.Type)
            {
                case RunType.Zeros:
                    buffer.Slice(totalCopied, toCopy).Clear();
                    break;

                case RunType.Raw:
                    _stream.Position = _activeRun.CompOffset + bufferOffset;
                    _stream.ReadExactly(buffer.Slice(totalCopied, toCopy));
                    break;

                case RunType.AdcCompressed:
                case RunType.ZlibCompressed:
                case RunType.BZlibCompressed:
                case RunType.LzfseCompressed:
                    _decompBuffer.AsSpan(bufferOffset, toCopy).CopyTo(buffer.Slice(totalCopied));
                    break;

                default:
                    throw new NotImplementedException($"Reading from run of type {_activeRun.Type}");
            }

            currentPos += toCopy;
            totalCopied += toCopy;
        }

        return totalCopied;
    }

    public override void Write(long pos, byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override void Write(long pos, ReadOnlySpan<byte> buffer) =>
        throw new NotSupportedException();

    public override void SetCapacity(long value)
    {
        throw new NotSupportedException();
    }

    public override IEnumerable<StreamExtent> GetExtentsInRange(long start, long count)
    {
        StreamExtent lastRun = default;

        foreach (var block in Blocks)
        {
            if ((block.FirstSector + block.SectorCount) * Sizes.Sector < start)
            {
                // Skip blocks before start of range
                continue;
            }

            if (block.FirstSector * Sizes.Sector > start + count)
            {
                // Skip blocks after end of range
                continue;
            }

            foreach (var run in block.Runs)
            {
                if (run.SectorCount > 0 && run.Type != RunType.Zeros)
                {
                    var thisRunStart = (block.FirstSector + run.SectorStart) * Sizes.Sector;
                    var thisRunEnd = thisRunStart + run.SectorCount * Sizes.Sector;

                    thisRunStart = Math.Max(thisRunStart, start);
                    thisRunEnd = Math.Min(thisRunEnd, start + count);

                    var thisRunLength = thisRunEnd - thisRunStart;

                    if (thisRunLength > 0)
                    {
                        if (lastRun.Start + lastRun.Length == thisRunStart)
                        {
                            lastRun = new StreamExtent(lastRun.Start, lastRun.Length + thisRunLength);
                        }
                        else
                        {
                            if (lastRun != default)
                            {
                                yield return lastRun;
                            }

                            lastRun = new StreamExtent(thisRunStart, thisRunLength);
                        }
                    }
                }
            }
        }

        if (lastRun != default)
        {
            yield return lastRun;
        }
    }

    private static int ADCDecompress(byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer,
                                     int outputOffset)
    {
        var consumed = 0;
        var written = 0;

        while (consumed < inputCount)
        {
            var focusByte = inputBuffer[inputOffset + consumed];
            if ((focusByte & 0x80) != 0)
            {
                // Data Run
                var chunkSize = (focusByte & 0x7F) + 1;
                System.Buffer.BlockCopy(inputBuffer, inputOffset + consumed + 1, outputBuffer, outputOffset + written, chunkSize);

                consumed += chunkSize + 1;
                written += chunkSize;
            }
            else if ((focusByte & 0x40) != 0)
            {
                // 3 byte code
                var chunkSize = (focusByte & 0x3F) + 4;
                int offset = EndianUtilities.ToUInt16BigEndian(inputBuffer, inputOffset + consumed + 1);

                for (var i = 0; i < chunkSize; ++i)
                {
                    outputBuffer[outputOffset + written + i] =
                        outputBuffer[outputOffset + written + i - offset - 1];
                }

                consumed += 3;
                written += chunkSize;
            }
            else
            {
                // 2 byte code
                var chunkSize = ((focusByte & 0x3F) >> 2) + 3;
                var offset = ((focusByte & 0x3) << 8) + (inputBuffer[inputOffset + consumed + 1] & 0xFF);

                for (var i = 0; i < chunkSize; ++i)
                {
                    outputBuffer[outputOffset + written + i] =
                        outputBuffer[outputOffset + written + i - offset - 1];
                }

                consumed += 2;
                written += chunkSize;
            }
        }

        return written;
    }

    private void LoadRun(long pos)
    {
        if (_activeRun != null
            && pos >= _activeRunOffset + _activeRun.SectorStart * Sizes.Sector
            && pos < _activeRunOffset + (_activeRun.SectorStart + _activeRun.SectorCount) * Sizes.Sector)
        {
            return;
        }

        var findSector = pos / 512;
        foreach (var block in Blocks)
        {
            if (block.FirstSector <= findSector && block.FirstSector + block.SectorCount > findSector)
            {
                // Make sure the decompression buffer is big enough
                if (_decompBuffer == null || _decompBuffer.Length < block.DecompressBufferRequested * Sizes.Sector)
                {
                    _decompBuffer = new byte[block.DecompressBufferRequested * Sizes.Sector];
                }

                foreach (var run in block.Runs)
                {
                    if (block.FirstSector + run.SectorStart <= findSector &&
                        block.FirstSector + run.SectorStart + run.SectorCount > findSector)
                    {
                        LoadRun(run);
                        _activeRunOffset = block.FirstSector * Sizes.Sector;
                        return;
                    }
                }

                throw new IOException($"No run for sector {findSector} in block starting at {block.FirstSector}");
            }
        }

        throw new IOException($"No block for sector {findSector}");
    }

    private async ValueTask LoadRunAsync(long pos, CancellationToken cancellationToken)
    {
        if (_activeRun != null
            && pos >= _activeRunOffset + _activeRun.SectorStart * Sizes.Sector
            && pos < _activeRunOffset + (_activeRun.SectorStart + _activeRun.SectorCount) * Sizes.Sector)
        {
            return;
        }

        var findSector = pos / 512;
        foreach (var block in Blocks)
        {
            if (block.FirstSector <= findSector && block.FirstSector + block.SectorCount > findSector)
            {
                // Make sure the decompression buffer is big enough
                if (_decompBuffer == null || _decompBuffer.Length < block.DecompressBufferRequested * Sizes.Sector)
                {
                    _decompBuffer = new byte[block.DecompressBufferRequested * Sizes.Sector];
                }

                foreach (var run in block.Runs)
                {
                    if (block.FirstSector + run.SectorStart <= findSector &&
                        block.FirstSector + run.SectorStart + run.SectorCount > findSector)
                    {
                        await LoadRunAsync(run, cancellationToken).ConfigureAwait(false);
                        _activeRunOffset = block.FirstSector * Sizes.Sector;
                        return;
                    }
                }

                throw new IOException($"No run for sector {findSector} in block starting at {block.FirstSector}");
            }
        }

        throw new IOException($"No block for sector {findSector}");
    }

    private void LoadRun(CompressedRun run)
    {
        var toCopy = (int)(run.SectorCount * Sizes.Sector);

        switch (run.Type)
        {
            case RunType.ZlibCompressed:
                _stream.Position = run.CompOffset + 2; // 2 byte zlib header
                using (var ds = new DeflateStream(_stream, CompressionMode.Decompress, true))
                {
                    ds.ReadExactly(_decompBuffer, 0, toCopy);
                }

                break;

            case RunType.AdcCompressed:
                _stream.Position = run.CompOffset;
                var compressed = _stream.ReadExactly((int)run.CompLength);
                if (ADCDecompress(compressed, 0, compressed.Length, _decompBuffer, 0) != toCopy)
                {
                    throw new InvalidDataException("Run too short when decompressed");
                }

                break;

            case RunType.BZlibCompressed:
                using (
                    var ds =
                        new BZip2DecoderStream(new SubStream(_stream, run.CompOffset, run.CompLength),
                            Ownership.None))
                {
                    ds.ReadExactly(_decompBuffer, 0, toCopy);
                }

                break;

            case RunType.LzfseCompressed:
                _stream.Position = run.CompOffset;
                var lzfseCompressed = _stream.ReadExactly((int)run.CompLength);
                if (Lzfse.LzfseCompressor.Decompress(lzfseCompressed, _decompBuffer) != toCopy)
                {
                    throw new InvalidDataException("Run too short when decompressed");
                }

                break;

            case RunType.Zeros:
            case RunType.Raw:
                break;

            default:
                throw new NotImplementedException($"Unrecognized run type {run.Type}");
        }

        _activeRun = run;
    }

    private async ValueTask LoadRunAsync(CompressedRun run, CancellationToken cancellationToken)
    {
        var toCopy = (int)(run.SectorCount * Sizes.Sector);

        switch (run.Type)
        {
            case RunType.ZlibCompressed:
                _stream.Position = run.CompOffset + 2; // 2 byte zlib header
                using (var ds = new DeflateStream(_stream, CompressionMode.Decompress, true))
                {
                    await ds.ReadExactlyAsync(_decompBuffer.AsMemory(0, toCopy), cancellationToken).ConfigureAwait(false);
                }

                break;

            case RunType.AdcCompressed:
                _stream.Position = run.CompOffset;
                var compressed = await _stream.ReadExactlyAsync((int)run.CompLength, cancellationToken).ConfigureAwait(false);
                if (ADCDecompress(compressed, 0, compressed.Length, _decompBuffer, 0) != toCopy)
                {
                    throw new InvalidDataException("Run too short when decompressed");
                }

                break;

            case RunType.BZlibCompressed:
                using (
                    var ds =
                        new BZip2DecoderStream(new SubStream(_stream, run.CompOffset, run.CompLength),
                            Ownership.None))
                {
                    await ds.ReadExactlyAsync(_decompBuffer.AsMemory(0, toCopy), cancellationToken).ConfigureAwait(false);
                }

                break;

            case RunType.LzfseCompressed:
                _stream.Position = run.CompOffset;
                var lzfseCompressed = await _stream.ReadExactlyAsync((int)run.CompLength, cancellationToken).ConfigureAwait(false);
                if (Lzfse.LzfseCompressor.Decompress(lzfseCompressed, _decompBuffer) != toCopy)
                {
                    throw new InvalidDataException("Run too short when decompressed");
                }

                break;

            case RunType.Zeros:
            case RunType.Raw:
                break;

            default:
                throw new NotImplementedException($"Unrecognized run type {run.Type}");
        }

        _activeRun = run;
    }
}