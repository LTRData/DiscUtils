// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Streams.Compatibility;
using DiscUtils.ExFat.Internal.Partition;
using LTRData.Extensions.Async;
using Buffer = System.Buffer;

namespace DiscUtils.ExFat.Internal.IO;
/// <inheritdoc />
/// <summary>
/// Stream, based on clusters chain
/// </summary>
/// <seealso cref="T:System.IO.Stream" />
public class ClusterStream : CompatibilityStream
{
    private readonly IClusterReader _clusterReader;
    private readonly IClusterWriter _clusterWriter;
    private Cluster _startCluster;
    private bool _contiguous;
    private readonly Action<DataDescriptor> _onDisposed;
    private long _dataLength;
    private long _validDataLength;
    private long _position;

    private byte[] _currentClusterBuffer;
    private long _currentClusterDataIndex = -1;
    private bool _currentClusterDirty;

    private long _currentClusterIndex = -2; // because -1 won't work in SeekClusterFromPosition
    private long CurrentClusterIndexFromPosition => _position / _clusterReader.BytesPerCluster;
    private Cluster _currentCluster;

    private int CurrentClusterOffset => (int)(_position % _clusterReader.BytesPerCluster);

    /// <inheritdoc />
    /// <summary>
    /// Gets a value indicating whether the current stream supports reading.
    /// </summary>
    public override bool CanRead => true;
    /// <inheritdoc />
    /// <summary>
    /// Gets a value indicating whether the current stream supports seeking.
    /// </summary>
    public override bool CanSeek => true;
    /// <inheritdoc />
    /// <summary>
    /// When overridden in a derived class, gets a value indicating whether the current stream supports writing.
    /// </summary>
    public override bool CanWrite => _clusterWriter != null;

    /// <inheritdoc />
    /// <summary>
    /// Gets the length in bytes of the stream.
    /// </summary>
    /// <exception cref="T:System.NotSupportedException"></exception>
    public override long Length => _validDataLength;

    /// <inheritdoc />
    /// <summary>
    /// Gets or sets the position within the current stream.
    /// </summary>
    public override long Position
    {
        get => Seek(0, SeekOrigin.Current);
        set => Seek(value, SeekOrigin.Begin);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="T:ExFat.IO.ClusterStream" /> class.
    /// </summary>
    /// <param name="clusterReader">The cluster information reader.</param>
    /// <param name="clusterWriter">The cluster writer.</param>
    /// <param name="dataDescriptor">The data descriptor.</param>
    /// <param name="onDisposed">Method invoked when stream is disposed.</param>
    /// <exception cref="T:System.ArgumentException">If contiguous is true, the length must be specified</exception>
    /// <inheritdoc />
    public ClusterStream(IClusterReader clusterReader, IClusterWriter clusterWriter, DataDescriptor dataDescriptor, Action<DataDescriptor> onDisposed)
    {
        _clusterReader = clusterReader;
        _clusterWriter = clusterWriter;
        _startCluster = dataDescriptor.FirstCluster;
        _contiguous = dataDescriptor.Contiguous;
        _onDisposed = onDisposed;
        _dataLength = (long)dataDescriptor.PhysicalLength;
        _validDataLength = (long)dataDescriptor.LogicalLength;

        _position = 0;
        _currentCluster = _startCluster;
    }

    /// <inheritdoc />
    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="T:System.IO.Stream" /> and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected override void Dispose(bool disposing)
    {
        FlushCurrentCluster();
        base.Dispose(disposing);
        if (disposing && _onDisposed != null)
        {
            _onDisposed(new DataDescriptor(_startCluster, _contiguous, (ulong)_dataLength, (ulong)_validDataLength));
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// Clears all buffers for this stream and causes any buffered data to be written to the underlying device.
    /// </summary>
    public override void Flush()
    {
        if (!CanWrite)
        {
            throw new NotSupportedException();
        }

        FlushCurrentCluster();
    }

    private void FlushCurrentCluster()
    {
        if (_currentClusterDirty)
        {
            _clusterWriter.WriteCluster(_currentCluster, _currentClusterBuffer, 0, _currentClusterBuffer.Length);
            _currentClusterDirty = false;
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// Clears all buffers for this stream and causes any buffered data to be written to the underlying device.
    /// </summary>
    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (!CanWrite)
        {
            throw new NotSupportedException();
        }

        if (_currentClusterDirty)
        {
            await _clusterWriter.WriteClusterAsync(_currentCluster, _currentClusterBuffer, 0, _currentClusterBuffer.Length, cancellationToken).ConfigureAwait(false);
            _currentClusterDirty = false;
        }
    }

    /// <summary>Sets the position within the current stream.</summary>
    /// <param name="offset">A byte offset relative to the <paramref name="origin" /> parameter. </param>
    /// <param name="origin">A value of type <see cref="T:System.IO.SeekOrigin" /> indicating the reference point used to obtain the new position. </param>
    /// <returns>The new position within the current stream.</returns>
    /// <exception cref="T:System.IO.IOException">An I/O error occurs. </exception>
    /// <exception cref="T:System.NotSupportedException">The stream does not support seeking, such as if the stream is constructed from a pipe or console output. </exception>
    /// <exception cref="T:System.ObjectDisposedException">Methods were called after the stream was closed. </exception>
    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        var newPosition = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _validDataLength + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null),
        };
        if (newPosition < 0)
        {
            newPosition = 0;
        }

        if (newPosition > _validDataLength)
        {
            newPosition = _validDataLength;
        }

        _position = newPosition;
        return _position;
    }

    /// <inheritdoc />
    /// <summary>
    /// Sets the length of the current stream.
    /// </summary>
    /// <param name="value">The desired length of the current stream in bytes.</param>
    /// <exception cref="T:System.NotImplementedException"></exception>
    public override void SetLength(long value)
    {
        if (!CanWrite || !CanSeek)
        {
            throw new NotSupportedException();
        }

        var position = Position;

        SetDataLength(value);
        SetValidDataLength(value);

        // adjust position if necessary
        Seek(position, SeekOrigin.Begin);
    }

    /// <summary>
    /// Sets the length of the data.
    /// This is preallocation
    /// </summary>
    /// <param name="dataLength">Length of the data.</param>
    public void SetDataLength(long dataLength)
    {
        if (!CanWrite || !CanSeek)
        {
            throw new NotSupportedException();
        }

        var position = Position;

        // first part: go to end, pushing limits if necessary
        var cluster = _startCluster;
        for (var offset = 0; offset < dataLength; offset += _clusterReader.BytesPerCluster)
        {
            _position = offset;
            SeekClusterFromPosition(true, true);
            cluster = _currentCluster;
        }
        // second part: trim excess (if any)
        for (var extraCluster = GetNextCluster(cluster, 1); extraCluster.IsData; extraCluster = GetNextCluster(extraCluster, 1))
        {
            _clusterWriter.FreeCluster(extraCluster);
            _clusterWriter.SetNextCluster(extraCluster, Cluster.Free);
        }

        _clusterWriter.SetNextCluster(cluster, Cluster.Last);

        // now adjust
        _dataLength = dataLength;
        if (_validDataLength > _dataLength)
        {
            _validDataLength = dataLength;
        }

        // adjust position if necessary
        Seek(position, SeekOrigin.Begin);
    }

    /// <summary>
    /// Sets the length of the valid data.
    /// This must be less than or equal to data length
    /// </summary>
    /// <param name="validDataLength">Length of the valid data.</param>
    public void SetValidDataLength(long validDataLength)
    {
        if (!CanWrite || !CanSeek)
        {
            throw new NotSupportedException();
        }

        var position = Position;

        if (validDataLength > _dataLength)
        {
            throw new ArgumentException("validDataLength must be be lower than or equal to data length", nameof(validDataLength));
        }

        _validDataLength = validDataLength;

        // adjust position if necessary
        Seek(position, SeekOrigin.Begin);
    }

    private void SeekClusterFromPosition(bool append, bool force)
    {
        var clusterIndexFromPosition = CurrentClusterIndexFromPosition;
        if (clusterIndexFromPosition == _currentClusterIndex && !force)
        {
            return;
        }

        FlushCurrentCluster();

        var currentCluster = GetClusterFromIndex(clusterIndexFromPosition);
        // most simple option: cluster is available or we don't need more
        if (currentCluster.IsData || !CanWrite || !append)
        {
            _currentCluster = currentCluster;
            _currentClusterIndex = clusterIndexFromPosition;
            return;
        }

        // overwise, allocate a new cluster

        // get the previous cluster, which is usually the current one
        var previousCluster = _currentClusterIndex == clusterIndexFromPosition - 1 ? _currentCluster : GetClusterFromIndex(clusterIndexFromPosition - 1);
        var newCluster = _clusterWriter.AllocateCluster(previousCluster);
        if (!previousCluster.IsData)
        {
            _startCluster = newCluster;
            _contiguous = true;
        }
        else
        {
            if (_contiguous && newCluster != previousCluster + 1)
            {
                _contiguous = false;
                for (var clusterIndex = 1; clusterIndex < clusterIndexFromPosition; clusterIndex++)
                {
                    _clusterWriter.SetNextCluster(_startCluster + clusterIndex - 1, _startCluster + clusterIndex);
                }
            }

            _clusterWriter.SetNextCluster(previousCluster, newCluster);
        }

        _clusterWriter.SetNextCluster(newCluster, Cluster.Last);
        _currentCluster = newCluster;
        _currentClusterIndex = clusterIndexFromPosition;
    }

    private byte[] GetSeekedCluster()
    {
        if (!_currentCluster.IsData)
        {
            return null;
        }

        if (_currentClusterBuffer == null)
        {
            _currentClusterBuffer = new byte[_clusterReader.BytesPerCluster];
            _currentClusterDataIndex = -1;
        }

        if (_currentClusterDataIndex == _currentClusterIndex)
        {
            return _currentClusterBuffer;
        }

        _currentClusterDataIndex = _currentClusterIndex;
        _clusterReader.ReadCluster(_currentCluster, _currentClusterBuffer, 0, _currentClusterBuffer.Length);
        return _currentClusterBuffer;
    }

    private async ValueTask<byte[]> GetSeekedClusterAsync(CancellationToken cancellationToken)
    {
        if (!_currentCluster.IsData)
        {
            return null;
        }

        if (_currentClusterBuffer == null)
        {
            _currentClusterBuffer = new byte[_clusterReader.BytesPerCluster];
            _currentClusterDataIndex = -1;
        }

        if (_currentClusterDataIndex == _currentClusterIndex)
        {
            return _currentClusterBuffer;
        }

        _currentClusterDataIndex = _currentClusterIndex;
        await _clusterReader.ReadClusterAsync(_currentCluster, _currentClusterBuffer, 0, _currentClusterBuffer.Length, cancellationToken).ConfigureAwait(false);
        return _currentClusterBuffer;
    }

    private Cluster GetClusterFromIndex(long index)
    {
        if (index < 0)
        {
            return Cluster.Last;
        }

        if (index == 0)
        {
            return _startCluster;
        }
        // -1 means buffer is new
        if (_currentClusterDataIndex == -1)
        {
            return GetNextCluster(_startCluster, index);
        }

        if (index > _currentClusterDataIndex)
        {
            return GetNextCluster(_currentCluster, index - _currentClusterDataIndex);
        }

        return GetNextCluster(_startCluster, index);
    }

    private Cluster GetNextCluster(Cluster cluster, long clustersCount)
    {
        if (clustersCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cluster), "cluster must be >= 0");
        }

        if (_contiguous)
        {
            var nextCluster = cluster + clustersCount;
            var lastContiguousCluster = _startCluster + ((_dataLength + _clusterReader.BytesPerCluster - 1) / _clusterReader.BytesPerCluster - 1);
            if (nextCluster.Value <= lastContiguousCluster.Value)
            {
                return nextCluster;
            }

            return Cluster.Last;
        }

        for (var index = 0; index < clustersCount && !cluster.IsLast; index++)
        {
            cluster = _clusterReader.GetNextCluster(cluster);
        }

        return cluster;
    }

    /// <inheritdoc />
    /// <summary>
    /// Reads a sequence of bytes from the current stream and advances the position within the stream by the number of bytes read.
    /// </summary>
    /// <param name="buffer">An array of bytes. When this method returns, the buffer contains the specified byte array with the values between <paramref name="offset" /> and (<paramref name="offset" /> + <paramref name="count" /> - 1) replaced by the bytes read from the current source.</param>
    /// <param name="offset">The zero-based byte offset in <paramref name="buffer" /> at which to begin storing the data read from the current stream.</param>
    /// <param name="count">The maximum number of bytes to be read from the current stream.</param>
    /// <returns>
    /// The total number of bytes read into the buffer. This can be less than the number of bytes requested if that many bytes are not currently available, or zero (0) if the end of the stream has been reached.
    /// </returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        var totalRead = 0;
        while (count > 0)
        {
            // what remaings in current cluster
            var remainingInCluster = _clusterReader.BytesPerCluster - CurrentClusterOffset;
            var toRead = Math.Min(remainingInCluster, count);
            var leftInFile = _validDataLength - _position;
            if (leftInFile == 0)
            {
                break;
            }

            if (toRead > leftInFile)
            {
                toRead = (int)leftInFile;
            }

            SeekClusterFromPosition(false, false);
            var currentCluster = GetSeekedCluster();
            // null means nothing left to read (for streams without length; with length we've exited before)
            if (currentCluster == null)
            {
                break;
            }

            Buffer.BlockCopy(currentCluster, CurrentClusterOffset, buffer, offset, toRead);
            _position += toRead;
            offset += toRead;
            count -= toRead;
            totalRead += toRead;
        }

        return totalRead;
    }

    /// <inheritdoc />
    /// <summary>
    /// Reads a sequence of bytes from the current stream and advances the position within the stream by the number of bytes read.
    /// </summary>
    /// <param name="buffer">An array of bytes. When this method returns, the buffer contains the specified byte array with the values between <paramref name="offset" /> and (<paramref name="offset" /> + <paramref name="count" /> - 1) replaced by the bytes read from the current source.</param>
    /// <param name="offset">The zero-based byte offset in <paramref name="buffer" /> at which to begin storing the data read from the current stream.</param>
    /// <param name="count">The maximum number of bytes to be read from the current stream.</param>
    /// <param name="callback"></param>
    /// <param name="state"></param>
    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state) =>
        ReadAsync(buffer, offset, count).AsAsyncResult(callback, state);

    /// <returns>
    /// The total number of bytes read into the buffer. This can be less than the number of bytes requested if that many bytes are not currently available, or zero (0) if the end of the stream has been reached.
    /// </returns>
    public override int EndRead(IAsyncResult asyncResult) => ((Task<int>)asyncResult).Result;

    /// <inheritdoc />
    /// <summary>
    /// Reads a sequence of bytes from the current stream and advances the position within the stream by the number of bytes read.
    /// </summary>
    /// <param name="buffer">An array of bytes. When this method returns, the buffer contains the specified byte array with the values between <paramref name="offset" /> and (<paramref name="offset" /> + <paramref name="count" /> - 1) replaced by the bytes read from the current source.</param>
    /// <param name="offset">The zero-based byte offset in <paramref name="buffer" /> at which to begin storing the data read from the current stream.</param>
    /// <param name="count">The maximum number of bytes to be read from the current stream.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>
    /// The total number of bytes read into the buffer. This can be less than the number of bytes requested if that many bytes are not currently available, or zero (0) if the end of the stream has been reached.
    /// </returns>
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (count > 0)
        {
            // what remaings in current cluster
            var remainingInCluster = _clusterReader.BytesPerCluster - CurrentClusterOffset;
            var toRead = Math.Min(remainingInCluster, count);
            var leftInFile = _validDataLength - _position;
            if (leftInFile == 0)
            {
                break;
            }

            if (toRead > leftInFile)
            {
                toRead = (int)leftInFile;
            }

            SeekClusterFromPosition(false, false);
            var currentCluster = await GetSeekedClusterAsync(cancellationToken).ConfigureAwait(false);
            // null means nothing left to read (for streams without length; with length we've exited before)
            if (currentCluster == null)
            {
                break;
            }

            Buffer.BlockCopy(currentCluster, CurrentClusterOffset, buffer, offset, toRead);
            _position += toRead;
            offset += toRead;
            count -= toRead;
            totalRead += toRead;
        }

        return totalRead;
    }

    /// <inheritdoc />
    /// <summary>
    /// Reads a sequence of bytes from the current stream and advances the position within the stream by the number of bytes read.
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>
    /// The total number of bytes read into the buffer. This can be less than the number of bytes requested if that many bytes are not currently available, or zero (0) if the end of the stream has been reached.
    /// </returns>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var totalRead = 0;
        while (buffer.Length > 0)
        {
            // what remaings in current cluster
            var remainingInCluster = _clusterReader.BytesPerCluster - CurrentClusterOffset;
            var toRead = Math.Min(remainingInCluster, buffer.Length);
            var leftInFile = _validDataLength - _position;
            if (leftInFile == 0)
            {
                break;
            }

            if (toRead > leftInFile)
            {
                toRead = (int)leftInFile;
            }

            SeekClusterFromPosition(false, false);
            var currentCluster = await GetSeekedClusterAsync(cancellationToken).ConfigureAwait(false);
            // null means nothing left to read (for streams without length; with length we've exited before)
            if (currentCluster == null)
            {
                break;
            }

            currentCluster.AsMemory(CurrentClusterOffset, toRead).CopyTo(buffer);
            _position += toRead;
            buffer = buffer[toRead..];
            totalRead += toRead;
        }

        return totalRead;
    }

    /// <inheritdoc />
    /// <summary>
    /// Reads a sequence of bytes from the current stream and advances the position within the stream by the number of bytes read.
    /// </summary>
    /// <param name="buffer"></param>
    /// <returns>
    /// The total number of bytes read into the buffer. This can be less than the number of bytes requested if that many bytes are not currently available, or zero (0) if the end of the stream has been reached.
    /// </returns>
    public override int Read(Span<byte> buffer)
    {
        var totalRead = 0;
        while (buffer.Length > 0)
        {
            // what remaings in current cluster
            var remainingInCluster = _clusterReader.BytesPerCluster - CurrentClusterOffset;
            var toRead = Math.Min(remainingInCluster, buffer.Length);
            var leftInFile = _validDataLength - _position;
            if (leftInFile == 0)
            {
                break;
            }

            if (toRead > leftInFile)
            {
                toRead = (int)leftInFile;
            }

            SeekClusterFromPosition(false, false);
            var currentCluster = GetSeekedCluster();
            // null means nothing left to read (for streams without length; with length we've exited before)
            if (currentCluster == null)
            {
                break;
            }

            currentCluster.AsSpan(CurrentClusterOffset, toRead).CopyTo(buffer);
            _position += toRead;
            buffer = buffer[toRead..];
            totalRead += toRead;
        }

        return totalRead;
    }

    /// <inheritdoc />
    /// <summary>
    /// Writes a sequence of bytes to the current stream and advances the current position within this stream by the number of bytes written.
    /// </summary>
    /// <param name="buffer">An array of bytes. This method copies <paramref name="count" /> bytes from <paramref name="buffer" /> to the current stream.</param>
    /// <param name="offset">The zero-based byte offset in <paramref name="buffer" /> at which to begin copying bytes to the current stream.</param>
    /// <param name="count">The number of bytes to be written to the current stream.</param>
    /// <exception cref="T:System.NotSupportedException"></exception>
    public override void Write(byte[] buffer, int offset, int count)
    {
        if (!CanWrite)
        {
            throw new NotSupportedException();
        }

        while (count > 0)
        {
            // write is limited by what remaings in current cluster
            var remainingInCluster = _clusterReader.BytesPerCluster - CurrentClusterOffset;
            var toWrite = Math.Min(remainingInCluster, count);
            SeekClusterFromPosition(true, false);
            var currentCluster = GetSeekedCluster();
            Buffer.BlockCopy(buffer, offset, currentCluster, CurrentClusterOffset, toWrite);
            _currentClusterDirty = true;
            _position += toWrite;
            // pushing the limits!
            if (_position > _validDataLength)
            {
                _validDataLength = _position;
            }

            if (_position > _dataLength)
            {
                _dataLength = _position;
            }

            offset += toWrite;
            count -= toWrite;
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// Writes a sequence of bytes to the current stream and advances the current position within this stream by the number of bytes written.
    /// </summary>
    /// <param name="buffer">An array of bytes. This method copies <paramref name="count" /> bytes from <paramref name="buffer" /> to the current stream.</param>
    /// <param name="offset">The zero-based byte offset in <paramref name="buffer" /> at which to begin copying bytes to the current stream.</param>
    /// <param name="count">The number of bytes to be written to the current stream.</param>
    /// <param name="callback"></param>
    /// <param name="state"></param>
    /// <exception cref="T:System.NotSupportedException"></exception>
    public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state) =>
        WriteAsync(buffer, offset, count).AsAsyncResult(callback, state);

    /// <summary>
    /// </summary>
    /// <param name="asyncResult"></param>
    public override void EndWrite(IAsyncResult asyncResult) => ((Task)asyncResult).Wait();

    /// <inheritdoc />
    /// <summary>
    /// Writes a sequence of bytes to the current stream and advances the current position within this stream by the number of bytes written.
    /// </summary>
    /// <param name="buffer">An array of bytes. This method copies <paramref name="count" /> bytes from <paramref name="buffer" /> to the current stream.</param>
    /// <param name="offset">The zero-based byte offset in <paramref name="buffer" /> at which to begin copying bytes to the current stream.</param>
    /// <param name="count">The number of bytes to be written to the current stream.</param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="T:System.NotSupportedException"></exception>
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (!CanWrite)
        {
            throw new NotSupportedException();
        }

        while (count > 0)
        {
            // write is limited by what remaings in current cluster
            var remainingInCluster = _clusterReader.BytesPerCluster - CurrentClusterOffset;
            var toWrite = Math.Min(remainingInCluster, count);
            SeekClusterFromPosition(true, false);
            var currentCluster = await GetSeekedClusterAsync(cancellationToken).ConfigureAwait(false);
            Buffer.BlockCopy(buffer, offset, currentCluster, CurrentClusterOffset, toWrite);
            _currentClusterDirty = true;
            _position += toWrite;
            // pushing the limits!
            if (_position > _validDataLength)
            {
                _validDataLength = _position;
            }

            if (_position > _dataLength)
            {
                _dataLength = _position;
            }

            offset += toWrite;
            count -= toWrite;
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// Writes a sequence of bytes to the current stream and advances the current position within this stream by the number of bytes written.
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="T:System.NotSupportedException"></exception>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!CanWrite)
        {
            throw new NotSupportedException();
        }

        while (buffer.Length > 0)
        {
            // write is limited by what remaings in current cluster
            var remainingInCluster = _clusterReader.BytesPerCluster - CurrentClusterOffset;
            var toWrite = Math.Min(remainingInCluster, buffer.Length);
            SeekClusterFromPosition(true, false);
            var currentCluster = await GetSeekedClusterAsync(cancellationToken).ConfigureAwait(false);
            buffer[..toWrite].CopyTo(currentCluster.AsMemory(CurrentClusterOffset));
            _currentClusterDirty = true;
            _position += toWrite;
            // pushing the limits!
            if (_position > _validDataLength)
            {
                _validDataLength = _position;
            }

            if (_position > _dataLength)
            {
                _dataLength = _position;
            }

            buffer = buffer[toWrite..];
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// Writes a sequence of bytes to the current stream and advances the current position within this stream by the number of bytes written.
    /// </summary>
    /// <param name="buffer"></param>
    /// <exception cref="T:System.NotSupportedException"></exception>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (!CanWrite)
        {
            throw new NotSupportedException();
        }

        while (buffer.Length > 0)
        {
            // write is limited by what remaings in current cluster
            var remainingInCluster = _clusterReader.BytesPerCluster - CurrentClusterOffset;
            var toWrite = Math.Min(remainingInCluster, buffer.Length);
            SeekClusterFromPosition(true, false);
            var currentCluster = GetSeekedCluster();
            buffer[..toWrite].CopyTo(currentCluster.AsSpan(CurrentClusterOffset));
            _currentClusterDirty = true;
            _position += toWrite;
            // pushing the limits!
            if (_position > _validDataLength)
            {
                _validDataLength = _position;
            }

            if (_position > _dataLength)
            {
                _dataLength = _position;
            }

            buffer = buffer[toWrite..];
        }
    }
}
