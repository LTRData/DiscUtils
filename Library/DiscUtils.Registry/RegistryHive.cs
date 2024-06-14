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
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiscUtils.Internal;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Registry;

/// <summary>
/// A registry hive.
/// </summary>
public class RegistryHive : IDisposable
{
    internal const int BinStart = 4 * Sizes.OneKiB;
    private readonly List<BinHeader> _bins;

    private Stream _fileStream;

    private readonly HiveHeader _header;
    private readonly Ownership _ownsStream;

    /// <summary>
    /// Initializes a new instance of the RegistryHive class.
    /// </summary>
    /// <param name="filePath">Path to registry hive file. This method will also open
    /// LOG1 and LOG2 files at the same location if there are pending changes to apply
    /// to the registry hive.</param>
    /// <param name="access">Specifies read-only or read/write access</param>
    /// <remarks>
    /// The created object does not assume ownership of the stream.
    /// </remarks>
    public RegistryHive(string filePath, FileAccess access)
        : this(new FileStream(filePath, FileMode.Open, access, access.HasFlag(FileAccess.Write) ? FileShare.None : FileShare.Read),
              Ownership.Dispose,
              OpenLogFiles(filePath, access).ToArray())
    {
    }

    private static IEnumerable<Stream> OpenLogFiles(string hivePath, FileAccess access)
    {
        var log = $"{hivePath}.LOG";

        var log1 = $"{log}1";
        if (File.Exists(log1))
        {
            yield return new FileStream(log1, FileMode.Open, access, access.HasFlag(FileAccess.Write) ? FileShare.None : FileShare.Read);

            var log2 = $"{log}2";
            if (File.Exists(log2))
            {
                yield return new FileStream(log2, FileMode.Open, access, access.HasFlag(FileAccess.Write) ? FileShare.None : FileShare.Read);
            }
        }
        else if (File.Exists(log))
        {
            yield return new FileStream(log, FileMode.Open, access, access.HasFlag(FileAccess.Write) ? FileShare.None : FileShare.Read);
        }
    }

    /// <summary>
    /// Initializes a new instance of the RegistryHive class.
    /// </summary>
    /// <param name="file">Path to registry hive file. This method will also open
    /// LOG1 and LOG2 files at the same location if there are pending changes to apply
    /// to the registry hive.</param>
    /// <param name="access">Specifies read-only or read/write access</param>
    /// <remarks>
    /// The created object does not assume ownership of the stream.
    /// </remarks>
    public RegistryHive(DiscFileInfo file, FileAccess access)
        : this(file.Open(FileMode.Open, access), Ownership.Dispose, OpenLogFiles(file, access).ToArray())
    {
    }

    private static IEnumerable<Stream> OpenLogFiles(DiscFileInfo hivePath, FileAccess access)
    {
        var log = hivePath.FileSystem.GetFileInfo($"{hivePath.FullName}.LOG");

        var log1 = log.FileSystem.GetFileInfo($"{log.FullName}1");
        if (log1.Exists)
        {
            yield return log1.Open(FileMode.Open, access);

            var log2 = log.FileSystem.GetFileInfo($"{log.FullName}2");
            if (log2.Exists)
            {
                yield return log2.Open(FileMode.Open, access);
            }
        }
        else if (log.Exists)
        {
            yield return log.Open(FileMode.Open, access);
        }
    }

    /// <summary>
    /// Initializes a new instance of the RegistryHive class.
    /// </summary>
    /// <param name="hive">The stream containing the registry hive.</param>
    /// <param name="logfiles">LOG1 and LOG2 streams to replay pending changes from</param>
    /// <remarks>
    /// The created object does not assume ownership of the streams.
    /// </remarks>
    public RegistryHive(Stream hive, params Stream[] logfiles)
        : this(hive, Ownership.None, logfiles) { }

    /// <summary>
    /// Initializes a new instance of the RegistryHive class.
    /// </summary>
    /// <param name="hive">The stream containing the registry hive.</param>
    /// <param name="ownership">Whether the new object assumes object of the stream.</param>
    /// <param name="logstreams">LOG1 and LOG2 streams to replay pending changes from</param>
    public RegistryHive(Stream hive, Ownership ownership, params Stream[] logstreams)
    {
        _fileStream = hive;
        _fileStream.Position = 0;
        _ownsStream = ownership;

        Span<byte> buffer = stackalloc byte[HiveHeader.HeaderSize];
        _fileStream.ReadExactly(buffer);

        _header = new();
        var headerSize = _header.ReadFrom(buffer, throwOnInvalidData: false);

        // If header validation failed or dirty state, look for transaction logs
        if (headerSize == 0 || _header.Sequence1 != _header.Sequence2)
        {
            var logs = logstreams?
                .Where(log => log.Length > 0x1000)
                .ToArray();

            if (logs is not null && logs.Length > 0)
            {
                // If we are opening a hive read-only, copy to an in-memory buffer
                // to be able to replay logs
                if (!_fileStream.CanWrite)
                {
                    var mem = new MemoryStream((int)_fileStream.Length);
                    _fileStream.Position = 0;
                    _fileStream.CopyTo(mem);
                    mem.Position = 0;
                    if (ownership == Ownership.Dispose)
                    {
                        _fileStream.Dispose();
                    }

                    _fileStream = mem;
                }

                // Open log files
                var logfiles = new LogFile[Math.Min(2, logs.Length)];
                
                logfiles[0] = new(logs[0]);

                if (logs.Length > 1)
                {
                    logfiles[1] = new(logs[1]);
                }

                // Sort log files in order of sequence
                if (logfiles.Length > 1 &&
                    logfiles[0].HiveHeader.Sequence1 >= logfiles[1].HiveHeader.Sequence1)
                {
                    (logfiles[0], logfiles[1]) = (logfiles[1], logfiles[0]);
                    (logs[0], logs[1]) = (logs[1], logs[0]);
                }

                // If hive header failed validation, recover from latest log
                if (headerSize == 0)
                {
                    var lastvalid = logfiles.LastOrDefault(logfile => logfile.HeaderValid)
                        ?? throw new RegistryCorruptException("Registry transaction logs are corrupt");

                    _header = lastvalid.HiveHeader;
                }

                int lastSequenceNumber;
                int maxPosition;

                // First log
                if (logfiles.Length > 0 &&
                    logfiles[0].HiveHeader.Sequence1 >= _header.Sequence2)
                {
                    (lastSequenceNumber, maxPosition) = logfiles[0].UpdateHive(_fileStream);

                    if (maxPosition > _header.Length)
                    {
                        _header.Length = maxPosition;
                    }

                    // Also a secondary log
                    if (logfiles.Length > 1 &&
                        logfiles[1].HiveHeader.Sequence1 > _header.Sequence2)
                    {
                        // If secondary log continues right after last record in first log
                        if (logfiles[1].HiveHeader.Sequence1 == lastSequenceNumber + 1)
                        {
                            (lastSequenceNumber, maxPosition) = logfiles[1].UpdateHive(_fileStream);

                            if (maxPosition > _header.Length)
                            {
                                _header.Length = maxPosition;
                            }
                        }
                        else
                        {
                            // Otherwise secondary log is invalid, just get the latest sequence number
                            // without actually replay the records and zero out the log file
                            lastSequenceNumber = Math.Max(logfiles[1].HiveHeader.Sequence2, lastSequenceNumber);

                            if (logs[1].CanWrite)
                            {
                                logs[1].SetLength(0);
                            }
                        }
                    }
                }
                // If only secondary log file is after hive in sequence
                else if (logfiles.Length > 1 &&
                    logfiles[1].HiveHeader.Sequence1 >= _header.Sequence2)
                {
                    (lastSequenceNumber, maxPosition) = logfiles[1].UpdateHive(_fileStream);

                    if (maxPosition > _header.Length)
                    {
                        _header.Length = maxPosition;
                    }
                }
                else
                {
                    throw new RegistryCorruptException("Registry transaction logs are corrupt");
                }

                // Store latest recovered sequence number in the hive header
                // and write this modified header to the hive file
                _header.Sequence1 = _header.Sequence2 = lastSequenceNumber + 1;
                _header.Timestamp = DateTime.UtcNow;
                _header.WriteTo(buffer);
                _fileStream.Position = 0;
                _fileStream.Write(buffer);
                _fileStream.Position = 0;
            }
            else if (_fileStream.CanWrite)
            {
                throw new RegistryCorruptException("Registry hive needs transaction logs to recover pending changes");
            }
        }

        if (ownership == Ownership.Dispose && logstreams is not null)
        {
            foreach (var log in logstreams)
            {
                log.Dispose();
            }
        }

        // Enumerate hbins
        _bins = [];
        var pos = 0;
        while (pos < _header.Length)
        {
            _fileStream.Position = BinStart + pos;
            _fileStream.ReadExactly(buffer.Slice(0, BinHeader.HeaderSize));
            var header = new BinHeader();
            header.ReadFrom(buffer);
            _bins.Add(header);

            pos += header.BinSize;
        }
    }

    /// <summary>
    /// Gets the root key in the registry hive.
    /// </summary>
    public RegistryKey Root => new(this, GetCell<KeyNodeCell>(_header.RootCell));

    /// <summary>
    /// Disposes of this instance, freeing any underlying stream (if any).
    /// </summary>
    public void Dispose(bool disposing)
    {
        if (_fileStream is not null && _ownsStream == Ownership.Dispose)
        {
            if (disposing)
            {
                _fileStream.Dispose();
            }

            _fileStream = null;
        }
    }

    /// <summary>
    /// Creates a new (empty) registry hive.
    /// </summary>
    /// <param name="stream">The stream to contain the new hive.</param>
    /// <returns>The new hive.</returns>
    /// <remarks>
    /// The returned object does not assume ownership of the stream.
    /// </remarks>
    public static RegistryHive Create(Stream stream)
    {
        return Create(stream, Ownership.None);
    }

    /// <summary>
    /// Creates a new (empty) registry hive.
    /// </summary>
    /// <param name="stream">The stream to contain the new hive.</param>
    /// <param name="ownership">Whether the returned object owns the stream.</param>
    /// <returns>The new hive.</returns>
    public static RegistryHive Create(Stream stream, Ownership ownership)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream), "Attempt to create registry hive in null stream");
        }

        // Construct a file with minimal structure - hive header, plus one (empty) bin
        var binHeader = new BinHeader
        {
            FileOffset = 0,
            BinSize = 4 * Sizes.OneKiB
        };

        var hiveHeader = new HiveHeader
        {
            Length = binHeader.BinSize
        };

        stream.Position = 0;

        Span<byte> hiveHeaderBuffer = stackalloc byte[hiveHeader.Size];
        hiveHeader.WriteTo(hiveHeaderBuffer);
        stream.Write(hiveHeaderBuffer);

        Span<byte> binHeaderBuffer = stackalloc byte[binHeader.Size];
        binHeader.WriteTo(binHeaderBuffer);
        stream.Position = BinStart;
        stream.Write(binHeaderBuffer);

        Span<byte> sizeBuffer = stackalloc byte[4];
        EndianUtilities.WriteBytesLittleEndian(binHeader.BinSize - binHeader.Size, sizeBuffer);
        stream.Write(sizeBuffer);

        // Make sure the file is initialized out to the end of the first bin
        stream.Position = BinStart + binHeader.BinSize - 1;
        stream.WriteByte(0);

        // Temporary hive to perform construction of higher-level structures
        var newHive = new RegistryHive(stream);
        var rootCell = new KeyNodeCell("root", -1)
        {
            Flags = RegistryKeyFlags.Normal | RegistryKeyFlags.Root
        };
        newHive.UpdateCell(rootCell, true);

        var sd = new RegistrySecurity("O:BAG:BAD:PAI(A;;KA;;;SY)(A;CI;KA;;;BA)");
        var secCell = new SecurityCell(sd);
        newHive.UpdateCell(secCell, true);
        secCell.NextIndex = secCell.Index;
        secCell.PreviousIndex = secCell.Index;
        newHive.UpdateCell(secCell, false);

        rootCell.SecurityIndex = secCell.Index;
        newHive.UpdateCell(rootCell, false);

        // Ref the root cell from the hive header
        hiveHeader.RootCell = rootCell.Index;
        hiveHeader.WriteTo(hiveHeaderBuffer);
        stream.Position = 0;
        stream.Write(hiveHeaderBuffer);

        // Finally, return the new hive
        return new RegistryHive(stream, ownership);
    }

    /// <summary>
    /// Creates a new (empty) registry hive.
    /// </summary>
    /// <param name="path">The file to create the new hive in.</param>
    /// <returns>The new hive.</returns>
    public static RegistryHive Create(string path)
    {
        var locator = new LocalFileLocator(string.Empty, useAsync: false);
        return Create(locator.Open(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None), Ownership.Dispose);
    }

    internal K GetCell<K>(int index)
        where K : Cell
    {
        var bin = GetBin(index);

        if (bin is not null)
        {
            return (K)bin.TryGetCell(index);
        }

        return null;
    }

    internal void FreeCell(int index)
    {
        var bin = GetBin(index);

        bin?.FreeCell(index);
    }

    internal int UpdateCell(Cell cell, bool canRelocate)
    {
        if (cell.Index == -1 && canRelocate)
        {
            cell.Index = AllocateRawCell(cell.Size);
        }

        var bin = GetBin(cell.Index);

        if (bin is not null)
        {
            if (bin.UpdateCell(cell))
            {
                return cell.Index;
            }

            if (canRelocate)
            {
                var oldCell = cell.Index;
                cell.Index = AllocateRawCell(cell.Size);
                bin = GetBin(cell.Index);
                if (!bin.UpdateCell(cell))
                {
                    cell.Index = oldCell;
                    throw new RegistryCorruptException("Failed to migrate cell to new location");
                }

                FreeCell(oldCell);
                return cell.Index;
            }

            throw new ArgumentException("Can't update cell, needs relocation but relocation disabled",
                nameof(canRelocate));
        }

        throw new RegistryCorruptException($"No bin found containing index: {cell.Index}");
    }

    internal Span<byte> RawCellData(int index, Span<byte> maxBytes)
    {
        var bin = GetBin(index);

        if (bin is not null)
        {
            return bin.ReadRawCellData(index, maxBytes);
        }

        return default;
    }

    internal bool WriteRawCellData(int index, ReadOnlySpan<byte> data)
    {
        var bin = GetBin(index);

        if (bin is not null)
        {
            return bin.WriteRawCellData(index, data);
        }

        throw new RegistryCorruptException($"No bin found containing index: {index}");
    }

    internal int AllocateRawCell(int capacity)
    {
        var minSize = MathUtilities.RoundUp(capacity + 4, 8); // Allow for size header and ensure multiple of 8

        // Incredibly inefficient algorithm...
        foreach (var binHeader in _bins)
        {
            var bin = LoadBin(binHeader);
            var cellIndex = bin.AllocateCell(minSize);

            if (cellIndex >= 0)
            {
                return cellIndex;
            }
        }

        var newBinHeader = AllocateBin(minSize);
        var newBin = LoadBin(newBinHeader);
        return newBin.AllocateCell(minSize);
    }

    private BinHeader? FindBin(int index)
    {
        if (index < 0)
        {
            return null;
        }

        var bin = _bins
            .TakeWhile(bin => bin.FileOffset <= index)
            .Select(bin => new BinHeader?(bin))
            .FirstOrDefault(bin => bin.Value.FileOffset + bin.Value.BinSize >= index);

        return bin;
    }

    private Bin GetBin(int cellIndex)
    {
        var binHeader = FindBin(cellIndex);

        if (binHeader is not null)
        {
            return LoadBin(binHeader.Value);
        }

        return null;
    }

    private Bin LoadBin(BinHeader binHeader)
    {
        _fileStream.Position = BinStart + binHeader.FileOffset;
        return new Bin(this, _fileStream);
    }

    private BinHeader AllocateBin(int minSize)
    {
        var lastBin = _bins[_bins.Count - 1];

        var newBinHeader = new BinHeader
        {
            FileOffset = lastBin.FileOffset + lastBin.BinSize
        };
        newBinHeader.BinSize = MathUtilities.RoundUp(minSize + newBinHeader.Size, 4 * Sizes.OneKiB);

        var buffer = ArrayPool<byte>.Shared.Rent(newBinHeader.Size);
        try
        {
            newBinHeader.WriteTo(buffer);
            _fileStream.Position = BinStart + newBinHeader.FileOffset;
            _fileStream.Write(buffer, 0, newBinHeader.Size);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        Span<byte> cellHeader = stackalloc byte[4];
        EndianUtilities.WriteBytesLittleEndian(newBinHeader.BinSize - newBinHeader.Size, cellHeader);
        _fileStream.Write(cellHeader);

        // Update hive with new length
        _header.Length = newBinHeader.FileOffset + newBinHeader.BinSize;
        _header.Timestamp = DateTime.UtcNow;
        _header.Sequence1++;
        _header.Sequence2++;
        _fileStream.Position = 0;
        Span<byte> hiveHeader = stackalloc byte[_header.Size];
        _fileStream.ReadExactly(hiveHeader);
        _header.WriteTo(hiveHeader);
        _fileStream.Position = 0;
        _fileStream.Write(hiveHeader);

        // Make sure the file is initialized to desired position
        _fileStream.Position = BinStart + _header.Length - 1;
        _fileStream.WriteByte(0);

        _bins.Add(newBinHeader);
        return newBinHeader;
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    ~RegistryHive()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}