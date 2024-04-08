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
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Internal;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Vmdk;

internal abstract class CommonSparseExtentStream : MappedStream
{
    /// <summary>
    /// Indicator to whether end-of-stream has been reached.
    /// </summary>
    protected bool _atEof;

    /// <summary>
    /// The current grain that's loaded into _grainTable.
    /// </summary>
    protected int _currentGrainTable;

    /// <summary>
    /// Offset of this extent within the disk.
    /// </summary>
    protected long _diskOffset;

    /// <summary>
    /// Stream containing the sparse extent.
    /// </summary>
    protected Stream _fileStream;

    /// <summary>
    /// The Global Directory for this extent.
    /// </summary>
    protected uint[] _globalDirectory;

    /// <summary>
    /// The data corresponding to the current grain (or null).
    /// </summary>
    protected byte[] _grainTable;

    /// <summary>
    /// Cache of recently used grain tables.
    /// </summary>
    private readonly ObjectCache<int, byte[]> _grainTableCache = new ObjectCache<int, byte[]>();

    /// <summary>
    /// The number of bytes controlled by a single grain table.
    /// </summary>
    protected long _gtCoverage;

    /// <summary>
    /// The header from the start of the extent.
    /// </summary>
    protected CommonSparseExtentHeader _header;

    /// <summary>
    /// Indicates if this object controls the lifetime of _fileStream.
    /// </summary>
    protected Ownership _ownsFileStream;

    /// <summary>
    /// Indicates if this object controls the lifetime of _parentDiskStream.
    /// </summary>
    protected Ownership _ownsParentDiskStream;

    /// <summary>
    /// The stream containing the unstored bytes.
    /// </summary>
    protected SparseStream _parentDiskStream;

    /// <summary>
    /// Current position in the extent.
    /// </summary>
    protected long _position;

    /// <summary>
    /// The Redundant Global Directory for this extent.
    /// </summary>
    protected uint[] _redundantGlobalDirectory;

    public override bool CanRead
    {
        get
        {
            CheckDisposed();
            return true;
        }
    }

    public override bool CanSeek
    {
        get
        {
            CheckDisposed();
            return true;
        }
    }

    public override bool CanWrite
    {
        get
        {
            CheckDisposed();
            return _fileStream.CanWrite;
        }
    }

    public override IEnumerable<StreamExtent> Extents
    {
        get { return GetExtentsInRange(0, Length); }
    }

    public override long Length
    {
        get
        {
            CheckDisposed();
            return _header.Capacity * Sizes.Sector;
        }
    }

    public override long Position
    {
        get
        {
            CheckDisposed();
            return _position;
        }

        set
        {
            CheckDisposed();
            _position = value;
            _atEof = false;
        }
    }

    public override void Flush()
    {
        CheckDisposed();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        CheckDisposed();

        if (_position > Length)
        {
            _atEof = true;
            throw new IOException("Attempt to read beyond end of stream");
        }

        if (_position == Length)
        {
            if (_atEof)
            {
                throw new IOException("Attempt to read beyond end of stream");
            }

            _atEof = true;
            return 0;
        }

        var maxToRead = (int)Math.Min(count, Length - _position);
        var totalRead = 0;
        int numRead;

        do
        {
            var grainTable = (int)(_position / _gtCoverage);
            var grainTableOffset = (int)(_position - grainTable * _gtCoverage);
            if (!LoadGrainTable(grainTable))
            {
                // Read from parent stream, to at most the end of grain table's coverage
                _parentDiskStream.Position = _position + _diskOffset;
                numRead = _parentDiskStream.Read(buffer, offset + totalRead,
                    (int)Math.Min(maxToRead - totalRead, _gtCoverage - grainTableOffset));
            }
            else
            {
                var grainSize = (int)(_header.GrainSize * Sizes.Sector);
                var grain = grainTableOffset / grainSize;
                var grainOffset = grainTableOffset - grain * grainSize;

                var numToRead = Math.Min(maxToRead - totalRead, grainSize - grainOffset);

                if (GetGrainTableEntry(grain) == 0)
                {
                    _parentDiskStream.Position = _position + _diskOffset;
                    numRead = _parentDiskStream.Read(buffer, offset + totalRead, numToRead);
                }
                else
                {
                    var bufferOffset = offset + totalRead;
                    var grainStart = (long)GetGrainTableEntry(grain) * Sizes.Sector;
                    numRead = ReadGrain(buffer, bufferOffset, grainStart, grainOffset, numToRead);
                }
            }

            _position += numRead;
            totalRead += numRead;
        } while (numRead != 0 && totalRead < maxToRead);

        return totalRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        CheckDisposed();

        if (_position > Length)
        {
            _atEof = true;
            throw new IOException("Attempt to read beyond end of stream");
        }

        if (_position == Length)
        {
            if (_atEof)
            {
                throw new IOException("Attempt to read beyond end of stream");
            }

            _atEof = true;
            return 0;
        }

        var maxToRead = (int)Math.Min(buffer.Length, Length - _position);
        var totalRead = 0;
        int numRead;

        do
        {
            var grainTable = (int)(_position / _gtCoverage);
            var grainTableOffset = (int)(_position - grainTable * _gtCoverage);
            if (!await LoadGrainTableAsync(grainTable, cancellationToken).ConfigureAwait(false))
            {
                // Read from parent stream, to at most the end of grain table's coverage
                _parentDiskStream.Position = _position + _diskOffset;
                numRead = await _parentDiskStream.ReadAsync(buffer.Slice(totalRead,
                    (int)Math.Min(maxToRead - totalRead, _gtCoverage - grainTableOffset)), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var grainSize = (int)(_header.GrainSize * Sizes.Sector);
                var grain = grainTableOffset / grainSize;
                var grainOffset = grainTableOffset - grain * grainSize;

                var numToRead = Math.Min(maxToRead - totalRead, grainSize - grainOffset);

                if (GetGrainTableEntry(grain) == 0)
                {
                    _parentDiskStream.Position = _position + _diskOffset;
                    numRead = await _parentDiskStream.ReadAsync(buffer.Slice(totalRead, numToRead), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var bufferOffset = totalRead;
                    var grainStart = (long)GetGrainTableEntry(grain) * Sizes.Sector;
                    numRead = await ReadGrainAsync(buffer.Slice(bufferOffset, numToRead), grainStart, grainOffset, cancellationToken).ConfigureAwait(false);
                }
            }

            _position += numRead;
            totalRead += numRead;
        } while (numRead != 0 && totalRead < maxToRead);

        return totalRead;
    }

    public override int Read(Span<byte> buffer)
    {
        CheckDisposed();

        if (_position > Length)
        {
            _atEof = true;
            throw new IOException("Attempt to read beyond end of stream");
        }

        if (_position == Length)
        {
            if (_atEof)
            {
                throw new IOException("Attempt to read beyond end of stream");
            }

            _atEof = true;
            return 0;
        }

        var maxToRead = (int)Math.Min(buffer.Length, Length - _position);
        var totalRead = 0;
        int numRead;

        do
        {
            var grainTable = (int)(_position / _gtCoverage);
            var grainTableOffset = (int)(_position - grainTable * _gtCoverage);
            if (!LoadGrainTable(grainTable))
            {
                // Read from parent stream, to at most the end of grain table's coverage
                _parentDiskStream.Position = _position + _diskOffset;
                numRead = _parentDiskStream.Read(buffer.Slice(totalRead,
                    (int)Math.Min(maxToRead - totalRead, _gtCoverage - grainTableOffset)));
            }
            else
            {
                var grainSize = (int)(_header.GrainSize * Sizes.Sector);
                var grain = grainTableOffset / grainSize;
                var grainOffset = grainTableOffset - grain * grainSize;

                var numToRead = Math.Min(maxToRead - totalRead, grainSize - grainOffset);

                if (GetGrainTableEntry(grain) == 0)
                {
                    _parentDiskStream.Position = _position + _diskOffset;
                    numRead = _parentDiskStream.Read(buffer.Slice(totalRead, numToRead));
                }
                else
                {
                    var bufferOffset = totalRead;
                    var grainStart = (long)GetGrainTableEntry(grain) * Sizes.Sector;
                    numRead = ReadGrain(buffer.Slice(bufferOffset, numToRead), grainStart, grainOffset);
                }
            }

            _position += numRead;
            totalRead += numRead;
        } while (numRead != 0 && totalRead < maxToRead);

        return totalRead;
    }

    public override void SetLength(long value)
    {
        CheckDisposed();

        if (Length != value)
        {
            throw new NotImplementedException("Cannot modify size of vmdk extent");
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        CheckDisposed();

        var effectiveOffset = offset;
        if (origin == SeekOrigin.Current)
        {
            effectiveOffset += _position;
        }
        else if (origin == SeekOrigin.End)
        {
            effectiveOffset += _header.Capacity * Sizes.Sector;
        }

        _atEof = false;

        if (effectiveOffset < 0)
        {
            throw new IOException("Attempt to move before beginning of disk");
        }

        _position = effectiveOffset;
        return _position;
    }

    public override IEnumerable<StreamExtent> GetExtentsInRange(long start, long count)
    {
        CheckDisposed();

        var maxCount = Math.Min(Length, start + count) - start;
        if (maxCount < 0)
        {
            return Array.Empty<StreamExtent>();
        }

        var parentExtents = _parentDiskStream.GetExtentsInRange(_diskOffset + start, maxCount);
        parentExtents = StreamExtent.Offset(parentExtents, -_diskOffset);

        var result = StreamExtent.Union(LayerExtents(start, maxCount), parentExtents);
        result = StreamExtent.Intersect(result, new StreamExtent(start, maxCount));
        return result;
    }

    public override IEnumerable<StreamExtent> MapContent(long start, long length)
    {
        CheckDisposed();

        if (start < Length)
        {
            var end = Math.Min(start + length, Length);

            var pos = start;

            do
            {
                var grainTable = (int)(pos / _gtCoverage);
                var grainTableOffset = (int)(pos - grainTable * _gtCoverage);

                if (LoadGrainTable(grainTable))
                {
                    var grainSize = (int)(_header.GrainSize * Sizes.Sector);
                    var grain = grainTableOffset / grainSize;
                    var grainOffset = grainTableOffset - grain * grainSize;

                    var numToRead = (int)Math.Min(end - pos, grainSize - grainOffset);

                    if (GetGrainTableEntry(grain) != 0)
                    {
                        var grainStart = (long)GetGrainTableEntry(grain) * Sizes.Sector;
                        yield return MapGrain(grainStart, grainOffset, numToRead);
                    }

                    pos += numToRead;
                }
                else
                {
                    pos = (grainTable + 1) * _gtCoverage;
                }
            } while (pos < end);
        }
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing)
            {
                if (_ownsFileStream == Ownership.Dispose && _fileStream != null)
                {
                    _fileStream.Dispose();
                }

                _fileStream = null;

                if (_ownsParentDiskStream == Ownership.Dispose && _parentDiskStream != null)
                {
                    _parentDiskStream.Dispose();
                }

                _parentDiskStream = null;
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }

    protected uint GetGrainTableEntry(int grain)
    {
        return EndianUtilities.ToUInt32LittleEndian(_grainTable, grain * 4);
    }

    protected void SetGrainTableEntry(int grain, uint value)
    {
        EndianUtilities.WriteBytesLittleEndian(value, _grainTable, grain * 4);
    }

    protected virtual int ReadGrain(byte[] buffer, int bufferOffset, long grainStart, int grainOffset, int numToRead)
    {
        _fileStream.Position = grainStart + grainOffset;
        return _fileStream.Read(buffer, bufferOffset, numToRead);
    }

    protected virtual ValueTask<int> ReadGrainAsync(Memory<byte> buffer, long grainStart, int grainOffset, CancellationToken cancellationToken)
    {
        _fileStream.Position = grainStart + grainOffset;
        return _fileStream.ReadAsync(buffer, cancellationToken);
    }

    protected virtual int ReadGrain(Span<byte> buffer, long grainStart, int grainOffset)
    {
        _fileStream.Position = grainStart + grainOffset;
        return _fileStream.Read(buffer);
    }

    protected virtual StreamExtent MapGrain(long grainStart, int grainOffset, int numToRead)
    {
        return new StreamExtent(grainStart + grainOffset, numToRead);
    }

    protected virtual void LoadGlobalDirectory()
    {
        var numGTs = (int)MathUtilities.Ceil(_header.Capacity * Sizes.Sector, _gtCoverage);

        _globalDirectory = new uint[numGTs];
        _fileStream.Position = _header.GdOffset * Sizes.Sector;
        var gdAsBytes = _fileStream.ReadExactly(numGTs * 4);
        for (var i = 0; i < _globalDirectory.Length; ++i)
        {
            _globalDirectory[i] = EndianUtilities.ToUInt32LittleEndian(gdAsBytes, i * 4);
        }
    }

    protected bool LoadGrainTable(int index)
    {
        // Current grain table, so early-out
        if (_grainTable != null && _currentGrainTable == index)
        {
            return true;
        }

        // This grain table not present in grain directory, so can't load it...
        if (_globalDirectory[index] == 0)
        {
            return false;
        }

        // Cached grain table?
        var cachedGrainTable = _grainTableCache[index];
        if (cachedGrainTable != null)
        {
            _currentGrainTable = index;
            _grainTable = cachedGrainTable;
            return true;
        }

        // Not cached, so read
        _fileStream.Position = (long)_globalDirectory[index] * Sizes.Sector;
        var newGrainTable = _fileStream.ReadExactly((int)_header.NumGTEsPerGT * 4);
        _currentGrainTable = index;
        _grainTable = newGrainTable;

        _grainTableCache[index] = newGrainTable;

        return true;
    }

    protected async ValueTask<bool> LoadGrainTableAsync(int index, CancellationToken cancellationToken)
    {
        // Current grain table, so early-out
        if (_grainTable != null && _currentGrainTable == index)
        {
            return true;
        }

        // This grain table not present in grain directory, so can't load it...
        if (_globalDirectory[index] == 0)
        {
            return false;
        }

        // Cached grain table?
        var cachedGrainTable = _grainTableCache[index];
        if (cachedGrainTable != null)
        {
            _currentGrainTable = index;
            _grainTable = cachedGrainTable;
            return true;
        }

        // Not cached, so read
        _fileStream.Position = (long)_globalDirectory[index] * Sizes.Sector;
        var newGrainTable = await _fileStream.ReadExactlyAsync((int)_header.NumGTEsPerGT * 4, cancellationToken).ConfigureAwait(false);
        _currentGrainTable = index;
        _grainTable = newGrainTable;

        _grainTableCache[index] = newGrainTable;

        return true;
    }

    protected void CheckDisposed()
    {
        if (_fileStream == null)
        {
            throw new ObjectDisposedException("CommonSparseExtentStream");
        }
    }

    private IEnumerable<StreamExtent> LayerExtents(long start, long count)
    {
        var maxPos = start + count;
        var pos = FindNextPresentGrain(MathUtilities.RoundDown(start, _header.GrainSize * Sizes.Sector), maxPos);
        while (pos < maxPos)
        {
            var end = FindNextAbsentGrain(pos, maxPos);
            yield return new StreamExtent(pos, end - pos);

            pos = FindNextPresentGrain(end, maxPos);
        }
    }

    private long FindNextPresentGrain(long pos, long maxPos)
    {
        var grainSize = (int)(_header.GrainSize * Sizes.Sector);

        var foundStart = false;
        while (pos < maxPos && !foundStart)
        {
            var grainTable = (int)(pos / _gtCoverage);

            if (!LoadGrainTable(grainTable))
            {
                pos += _gtCoverage;
            }
            else
            {
                var grainTableOffset = (int)(pos - grainTable * _gtCoverage);

                var grain = grainTableOffset / grainSize;

                if (GetGrainTableEntry(grain) == 0)
                {
                    pos += grainSize;
                }
                else
                {
                    foundStart = true;
                }
            }
        }

        return Math.Min(pos, maxPos);
    }

    private long FindNextAbsentGrain(long pos, long maxPos)
    {
        var grainSize = (int)(_header.GrainSize * Sizes.Sector);

        var foundEnd = false;
        while (pos < maxPos && !foundEnd)
        {
            var grainTable = (int)(pos / _gtCoverage);

            if (!LoadGrainTable(grainTable))
            {
                foundEnd = true;
            }
            else
            {
                var grainTableOffset = (int)(pos - grainTable * _gtCoverage);

                var grain = grainTableOffset / grainSize;

                if (GetGrainTableEntry(grain) == 0)
                {
                    foundEnd = true;
                }
                else
                {
                    pos += grainSize;
                }
            }
        }

        return Math.Min(pos, maxPos);
    }
}