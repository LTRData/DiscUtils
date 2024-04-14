//
// Copyright (c) 2008-2012, Kenneth Bell
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

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Vhdx;

/// <summary>
/// Represents a chunk of blocks in the Block Allocation Table.
/// </summary>
/// <remarks>
/// The BAT entries for a chunk are always present in the BAT, but the data blocks and
/// sector bitmap blocks may (or may not) be present.
/// </remarks>
public sealed class Chunk
{
    public const ulong SectorBitmapPresent = 6;

    private readonly Stream _bat;
    private readonly byte[] _batData;
    private readonly int _blocksPerChunk;
    private readonly int _chunk;
    private readonly SparseStream _file;
    private readonly FileParameters _fileParameters;
    private readonly FreeSpaceTable _freeSpace;
    private byte[] _sectorBitmap;

    internal Chunk(Stream bat, SparseStream file, FreeSpaceTable freeSpace, FileParameters fileParameters, int chunk,
                 int blocksPerChunk)
    {
        _bat = bat;
        _file = file;
        _freeSpace = freeSpace;
        _fileParameters = fileParameters;
        _chunk = chunk;
        _blocksPerChunk = blocksPerChunk;

        _bat.Position = _chunk * (_blocksPerChunk + 1) * 8;
        _batData = bat.ReadExactly((_blocksPerChunk + 1) * 8);
    }

    public bool HasSectorBitmap => new BatEntry(_batData, _blocksPerChunk * 8).BitmapBlockPresent;

    private long SectorBitmapPos
    {
        get => new BatEntry(_batData, _blocksPerChunk * 8).FileOffsetMB * Sizes.OneMiB;

        set
        {
            var entry = new BatEntry
            {
                BitmapBlockPresent = value != 0,
                FileOffsetMB = value / Sizes.OneMiB
            };
            entry.WriteTo(_batData, _blocksPerChunk * 8);
        }
    }

    public int BlocksPerChunk => _blocksPerChunk;

    public long GetBlockPosition(int block)
    {
        return new BatEntry(_batData, block * 8).FileOffsetMB * Sizes.OneMiB;
    }

    public PayloadBlockStatus GetBlockStatus(int block)
    {
        return new BatEntry(_batData, block * 8).PayloadBlockStatus;
    }

    public AllocationBitmap GetBlockBitmap(int block)
    {
        var bytesPerBlock = (int)(Sizes.OneMiB / _blocksPerChunk);
        var offset = bytesPerBlock * block;
        var data = LoadSectorBitmap();
        return new AllocationBitmap(data, offset, bytesPerBlock);
    }

    public async ValueTask<AllocationBitmap> GetBlockBitmapAsync(int block, CancellationToken cancellationToken)
    {
        var bytesPerBlock = (int)(Sizes.OneMiB / _blocksPerChunk);
        var offset = bytesPerBlock * block;
        var data = await LoadSectorBitmapAsync(cancellationToken).ConfigureAwait(false);
        return new AllocationBitmap(data, offset, bytesPerBlock);
    }

    public void WriteBlockBitmap(int block)
    {
        var bytesPerBlock = (int)(Sizes.OneMiB / _blocksPerChunk);
        var offset = bytesPerBlock * block;

        _file.Position = SectorBitmapPos + offset;
        _file.Write(_sectorBitmap, offset, bytesPerBlock);
    }

    internal PayloadBlockStatus AllocateSpaceForBlock(int block)
    {
        var dataModified = false;

        var blockEntry = new BatEntry(_batData, block * 8);
        if (blockEntry.FileOffsetMB == 0)
        {
            blockEntry.FileOffsetMB = AllocateSpace((int)_fileParameters.BlockSize, false) / Sizes.OneMiB;
            dataModified = true;
        }

        if (blockEntry.PayloadBlockStatus is not PayloadBlockStatus.FullyPresent
            and not PayloadBlockStatus.PartiallyPresent)
        {
            if ((_fileParameters.Flags & FileParametersFlags.HasParent) != 0)
            {
                if (!HasSectorBitmap)
                {
                    SectorBitmapPos = AllocateSpace((int)Sizes.OneMiB, true);
                }

                blockEntry.PayloadBlockStatus = PayloadBlockStatus.PartiallyPresent;
            }
            else
            {
                blockEntry.PayloadBlockStatus = PayloadBlockStatus.FullyPresent;
            }

            dataModified = true;
        }

        if (dataModified)
        {
            blockEntry.WriteTo(_batData, block * 8);

            _bat.Position = _chunk * (_blocksPerChunk + 1) * 8;
            _bat.Write(_batData, 0, (_blocksPerChunk + 1) * 8);
        }

        return blockEntry.PayloadBlockStatus;
    }

    private byte[] LoadSectorBitmap()
    {
        if (_sectorBitmap == null)
        {
            _file.Position = SectorBitmapPos;
            _sectorBitmap = _file.ReadExactly((int)Sizes.OneMiB);
        }

        return _sectorBitmap;
    }

    private async ValueTask<byte[]> LoadSectorBitmapAsync(CancellationToken cancellationToken)
    {
        if (_sectorBitmap == null)
        {
            _file.Position = SectorBitmapPos;
            _sectorBitmap = await _file.ReadExactlyAsync((int)Sizes.OneMiB, cancellationToken).ConfigureAwait(false);
        }

        return _sectorBitmap;
    }

    private long AllocateSpace(int sizeBytes, bool zero)
    {
        if (!_freeSpace.TryAllocate(sizeBytes, out var pos))
        {
            pos = MathUtilities.RoundUp(_file.Length, Sizes.OneMiB);
            _file.SetLength(pos + sizeBytes);
            _freeSpace.ExtendTo(pos + sizeBytes, false);
        }
        else if (zero)
        {
            _file.Position = pos;
            _file.Clear(sizeBytes);
        }

        return pos;
    }
}