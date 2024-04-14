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

using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DiscUtils.Fat;

internal class ClusterStream : CompatibilityStream
{
    private readonly FileAccess _access;
    private readonly byte[] _clusterBuffer;
    private readonly FileAllocationTable _fat;

    private readonly List<uint> _knownClusters;
    private readonly ClusterReader _reader;

    private bool _atEOF;

    private uint _currentCluster;
    private uint _length;
    private long _position;

    internal ClusterStream(FatFileSystem fileSystem, FileAccess access, uint firstCluster, uint length)
    {
        _access = access;
        _reader = fileSystem.ClusterReader;
        _fat = fileSystem.Fat;
        _length = length;

        _knownClusters = new List<uint>();
        if (firstCluster != 0)
        {
            _knownClusters.Add(firstCluster);
        }
        else
        {
            _knownClusters.Add(FatBuffer.EndOfChain);
        }

        if (_length == uint.MaxValue)
        {
            _length = DetectLength();
        }

        _currentCluster = uint.MaxValue;
        _clusterBuffer = new byte[_reader.ClusterSize];
    }

    public override bool CanRead => _access is FileAccess.Read or FileAccess.ReadWrite;

    public override bool CanSeek => true;

    public override bool CanWrite => _access is FileAccess.ReadWrite or FileAccess.Write;

    public override long Length => _length;

    public override long Position
    {
        get => _position;

        set
        {
            if (value >= 0)
            {
                _position = value;
                _atEOF = false;
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Attempt to move before beginning of stream");
            }
        }
    }

    public event FirstClusterChangedDelegate FirstClusterChanged;

    public override void Flush() { }

    public override Task FlushAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (!CanRead)
        {
            throw new IOException("Attempt to read from file not opened for read");
        }

        if (_position > _length)
        {
            throw new IOException("Attempt to read beyond end of file");
        }

        var target = buffer.Length;
        if (_length - _position < buffer.Length)
        {
            target = (int)(_length - _position);
        }

        if (!TryLoadCurrentCluster())
        {
            if ((_position == _length || _position == DetectLength()) && !_atEOF)
            {
                _atEOF = true;
                return 0;
            }

            throw new IOException("Attempt to read beyond known clusters");
        }

        var numRead = 0;
        while (numRead < target)
        {
            var clusterOffset = (int)(_position % _reader.ClusterSize);
            var toCopy = Math.Min(_reader.ClusterSize - clusterOffset, target - numRead);
            _clusterBuffer.AsSpan(clusterOffset, toCopy).CopyTo(buffer.Slice(numRead));

            // Remember how many we've read in total
            numRead += toCopy;

            // Increment the position
            _position += toCopy;

            // Abort if we've hit the end of the file
            if (!TryLoadCurrentCluster())
            {
                break;
            }
        }

        if (numRead == 0)
        {
            _atEOF = true;
        }

        return numRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (!CanRead)
        {
            throw new IOException("Attempt to read from file not opened for read");
        }

        if (_position > _length)
        {
            throw new IOException("Attempt to read beyond end of file");
        }

        var target = buffer.Length;
        if (_length - _position < buffer.Length)
        {
            target = (int)(_length - _position);
        }

        if (!await TryLoadCurrentClusterAsync(cancellationToken).ConfigureAwait(false))
        {
            if ((_position == _length || _position == DetectLength()) && !_atEOF)
            {
                _atEOF = true;
                return 0;
            }

            throw new IOException("Attempt to read beyond known clusters");
        }

        var numRead = 0;
        while (numRead < target)
        {
            var clusterOffset = (int)(_position % _reader.ClusterSize);
            var toCopy = Math.Min(_reader.ClusterSize - clusterOffset, target - numRead);
            _clusterBuffer.AsMemory(clusterOffset, toCopy).CopyTo(buffer.Slice(numRead));

            // Remember how many we've read in total
            numRead += toCopy;

            // Increment the position
            _position += toCopy;

            // Abort if we've hit the end of the file
            if (!await TryLoadCurrentClusterAsync(cancellationToken).ConfigureAwait(false))
            {
                break;
            }
        }

        if (numRead == 0)
        {
            _atEOF = true;
        }

        return numRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var newPos = offset;
        if (origin == SeekOrigin.Current)
        {
            newPos += _position;
        }
        else if (origin == SeekOrigin.End)
        {
            newPos += Length;
        }

        _position = newPos;
        _atEOF = false;
        return newPos;
    }

    public override void SetLength(long value)
    {
        var desiredNumClusters = (value + _reader.ClusterSize - 1) / _reader.ClusterSize;
        var actualNumClusters = (_length + _reader.ClusterSize - 1) / _reader.ClusterSize;

        if (desiredNumClusters < actualNumClusters)
        {
            if (!TryGetClusterByPosition(value, out var cluster))
            {
                throw new IOException("Internal state corrupt - unable to find cluster");
            }

            var firstToFree = _fat.GetNext(cluster);
            _fat.SetEndOfChain(cluster);
            _fat.FreeChain(firstToFree);

            while (_knownClusters.Count > desiredNumClusters)
            {
                _knownClusters.RemoveAt(_knownClusters.Count - 1);
            }

            _knownClusters.Add(FatBuffer.EndOfChain);

            if (desiredNumClusters == 0)
            {
                FireFirstClusterAllocated(0);
            }
        }
        else if (desiredNumClusters > actualNumClusters)
        {
            while (!TryGetClusterByPosition(value, out var cluster))
            {
                cluster = ExtendChain();
                _reader.WipeCluster(cluster);
            }
        }

        if (_length != value)
        {
            _length = (uint)value;
            if (_position > _length)
            {
                _position = _length;
            }
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
        => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        var bytesRemaining = buffer.Length;

        if (!CanWrite)
        {
            throw new IOException("Attempting to write to file not opened for writing");
        }

        // TODO: Free space check...
        try
        {
            while (bytesRemaining > 0)
            {
                // Extend the stream until it encompasses _position
                uint cluster;
                while (!TryGetClusterByPosition(_position, out cluster))
                {
                    cluster = ExtendChain();
                    _reader.WipeCluster(cluster);
                }

                // Fill this cluster with as much data as we can (WriteToCluster preserves existing cluster
                // data, if necessary)
                var numWritten = WriteToCluster(cluster, (int)(_position % _reader.ClusterSize), buffer.Slice(0, bytesRemaining));
                buffer = buffer.Slice(numWritten);
                bytesRemaining -= numWritten;
                _position += numWritten;
            }

            _length = (uint)Math.Max(_length, _position);
        }
        finally
        {
            _fat.Flush();
        }

        _atEOF = false;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var bytesRemaining = buffer.Length;

        if (!CanWrite)
        {
            throw new IOException("Attempting to write to file not opened for writing");
        }

        // TODO: Free space check...
        try
        {
            while (bytesRemaining > 0)
            {
                // Extend the stream until it encompasses _position
                uint cluster;
                while (!TryGetClusterByPosition(_position, out cluster))
                {
                    cluster = ExtendChain();
                    await _reader.WipeClusterAsync(cluster, cancellationToken).ConfigureAwait(false);
                }

                // Fill this cluster with as much data as we can (WriteToCluster preserves existing cluster
                // data, if necessary)
                var numWritten = await WriteToClusterAsync(cluster, (int)(_position % _reader.ClusterSize), buffer.Slice(0, bytesRemaining), cancellationToken).ConfigureAwait(false);
                buffer = buffer.Slice(numWritten);
                bytesRemaining -= numWritten;
                _position += numWritten;
            }

            _length = (uint)Math.Max(_length, _position);
        }
        finally
        {
            await _fat.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        _atEOF = false;
    }

    /// <summary>
    /// Writes up to the next cluster boundary, making sure to preserve existing data in the cluster
    /// that falls outside of the updated range.
    /// </summary>
    /// <param name="cluster">The cluster to write to.</param>
    /// <param name="pos">The file position of the write (within the cluster).</param>
    /// <param name="buffer">The buffer with the new data.</param>
    /// <returns>The number of bytes written - either count, or the number that fit up to
    /// the cluster boundary.</returns>
    private int WriteToCluster(uint cluster, int pos, ReadOnlySpan<byte> buffer)
    {
        if (pos == 0 && buffer.Length >= _reader.ClusterSize)
        {
            _currentCluster = cluster;
            buffer.Slice(0, _reader.ClusterSize).CopyTo(_clusterBuffer);

            WriteCurrentCluster();

            return _reader.ClusterSize;
        }

        // Partial cluster, so need to read existing cluster data first
        LoadCluster(cluster);

        var copyLength = Math.Min(buffer.Length, _reader.ClusterSize - pos % _reader.ClusterSize);
        buffer.Slice(0, copyLength).CopyTo(_clusterBuffer.AsSpan(pos));

        WriteCurrentCluster();

        return copyLength;
    }

    /// <summary>
    /// Writes up to the next cluster boundary, making sure to preserve existing data in the cluster
    /// that falls outside of the updated range.
    /// </summary>
    /// <param name="cluster">The cluster to write to.</param>
    /// <param name="pos">The file position of the write (within the cluster).</param>
    /// <param name="buffer">The buffer with the new data.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The number of bytes written - either count, or the number that fit up to
    /// the cluster boundary.</returns>
    private async ValueTask<int> WriteToClusterAsync(uint cluster, int pos, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        if (pos == 0 && buffer.Length >= _reader.ClusterSize)
        {
            _currentCluster = cluster;
            buffer.Slice(0, _reader.ClusterSize).CopyTo(_clusterBuffer);

            await WriteCurrentClusterAsync(cancellationToken).ConfigureAwait(false);

            return _reader.ClusterSize;
        }

        // Partial cluster, so need to read existing cluster data first
        await LoadClusterAsync(cluster, cancellationToken).ConfigureAwait(false);

        var copyLength = Math.Min(buffer.Length, _reader.ClusterSize - pos % _reader.ClusterSize);
        buffer.Slice(0, copyLength).CopyTo(_clusterBuffer.AsMemory(pos));

        await WriteCurrentClusterAsync(cancellationToken).ConfigureAwait(false);

        return copyLength;
    }

    /// <summary>
    /// Adds a new cluster to the end of the existing chain, by allocating a free cluster.
    /// </summary>
    /// <returns>The cluster allocated.</returns>
    /// <remarks>This method does not initialize the data in the cluster, the caller should
    /// perform a write to ensure the cluster data is in known state.</remarks>
    private uint ExtendChain()
    {
        // Sanity check - make sure the final known cluster is the EOC marker
        if (!_fat.IsEndOfChain(_knownClusters[_knownClusters.Count - 1]))
        {
            throw new IOException("Corrupt file system: final cluster isn't End-of-Chain");
        }

        if (!_fat.TryGetFreeCluster(out var cluster))
        {
            throw new IOException("Out of disk space");
        }

        _fat.SetEndOfChain(cluster);
        if (_knownClusters.Count == 1)
        {
            FireFirstClusterAllocated(cluster);
        }
        else
        {
            _fat.SetNext(_knownClusters[_knownClusters.Count - 2], cluster);
        }

        _knownClusters[_knownClusters.Count - 1] = cluster;
        _knownClusters.Add(_fat.GetNext(cluster));

        return cluster;
    }

    private void FireFirstClusterAllocated(uint cluster)
    {
        if (FirstClusterChanged != null)
        {
            FirstClusterChanged(cluster);
        }
    }

    private bool TryLoadCurrentCluster()
    {
        return TryLoadClusterByPosition(_position);
    }

    private ValueTask<bool> TryLoadCurrentClusterAsync(CancellationToken cancellationToken)
    {
        return TryLoadClusterByPositionAsync(_position, cancellationToken);
    }

    private bool TryLoadClusterByPosition(long pos)
    {
        if (!TryGetClusterByPosition(pos, out var cluster))
        {
            return false;
        }

        // Read the cluster, it's different to the one currently loaded
        if (cluster != _currentCluster)
        {
            _reader.ReadCluster(cluster, _clusterBuffer, 0);
            _currentCluster = cluster;
        }

        return true;
    }

    private async ValueTask<bool> TryLoadClusterByPositionAsync(long pos, CancellationToken cancellationToken)
    {
        if (!TryGetClusterByPosition(pos, out var cluster))
        {
            return false;
        }

        // Read the cluster, it's different to the one currently loaded
        if (cluster != _currentCluster)
        {
            await _reader.ReadClusterAsync(cluster, _clusterBuffer, cancellationToken).ConfigureAwait(false);
            _currentCluster = cluster;
        }

        return true;
    }

    private void LoadCluster(uint cluster)
    {
        // Read the cluster, it's different to the one currently loaded
        if (cluster != _currentCluster)
        {
            _reader.ReadCluster(cluster, _clusterBuffer, 0);
            _currentCluster = cluster;
        }
    }

    private async ValueTask LoadClusterAsync(uint cluster, CancellationToken cancellationToken)
    {
        // Read the cluster, it's different to the one currently loaded
        if (cluster != _currentCluster)
        {
            await _reader.ReadClusterAsync(cluster, _clusterBuffer, cancellationToken).ConfigureAwait(false);
            _currentCluster = cluster;
        }
    }

    private void WriteCurrentCluster()
    {
        _reader.WriteCluster(_currentCluster, _clusterBuffer, 0);
    }

    private ValueTask WriteCurrentClusterAsync(CancellationToken cancellationToken)
    {
        return _reader.WriteClusterAsync(_currentCluster, _clusterBuffer, cancellationToken);
    }

    private bool TryGetClusterByPosition(long pos, out uint cluster)
    {
        var index = (int)(pos / _reader.ClusterSize);

        if (_knownClusters.Count <= index)
        {
            if (!TryPopulateKnownClusters(index))
            {
                cluster = uint.MaxValue;
                return false;
            }
        }

        // Chain is shorter than the current stream position
        if (_knownClusters.Count <= index)
        {
            cluster = uint.MaxValue;
            return false;
        }

        cluster = _knownClusters[index];

        // This is the 'special' End-of-chain cluster identifer, so the stream position
        // is greater than the actual file length.
        if (_fat.IsEndOfChain(cluster))
        {
            return false;
        }

        return true;
    }

    private bool TryPopulateKnownClusters(int index)
    {
        var lastKnown = _knownClusters[_knownClusters.Count - 1];
        while (!_fat.IsEndOfChain(lastKnown) && _knownClusters.Count <= index)
        {
            lastKnown = _fat.GetNext(lastKnown);
            _knownClusters.Add(lastKnown);
        }

        return _knownClusters.Count > index;
    }

    private uint DetectLength()
    {
        while (!_fat.IsEndOfChain(_knownClusters[_knownClusters.Count - 1]))
        {
            if (!TryPopulateKnownClusters(_knownClusters.Count))
            {
                throw new IOException("Corrupt file stream - unable to discover end of cluster chain");
            }
        }

        return (uint)((_knownClusters.Count - 1) * (long)_reader.ClusterSize);
    }

    public IEnumerable<Range<long, long>> EnumerateAllocatedClusters()
    {
        uint? firstCluster = null;
        uint? lastCluster = null;

        for (var i = 0; i < _knownClusters.Count && !_fat.IsEndOfChain(_knownClusters[i]); i++)
        {
            firstCluster ??= _knownClusters[i];

            if (lastCluster == null
                || _knownClusters[i] == lastCluster.Value + 1)
            {
                lastCluster = _knownClusters[i];
                continue;
            }

            yield return new(firstCluster.Value, lastCluster.Value - firstCluster.Value + 1);

            firstCluster = null;
            lastCluster = null;
        }
    }
}