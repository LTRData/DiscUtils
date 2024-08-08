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
using LTRData.Extensions.Buffers;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace DiscUtils.Archives;

/// <summary>
/// Minimal tar file format implementation.
/// </summary>
public class TarFile : IDisposable
{
    public static int FileDatabufferChunkSize { get; set; } = 32 * 1024 * 1024;

    private readonly Stream _fileStream;
    private readonly Dictionary<string, TarFileRecord> _files;
    private bool disposedValue;

    /// <summary>
    /// Initializes a new instance of the TarFile class.
    /// </summary>
    /// <param name="fileStream">The Tar file.</param>
    public TarFile(Stream fileStream)
    {
        _fileStream = fileStream;
        _files = new Dictionary<string, TarFileRecord>(StringComparer.Ordinal);

        Span<byte> hdrBuf = stackalloc byte[512];

        string long_path = null;

        for (;;)
        {                
            if (_fileStream.ReadMaximum(hdrBuf) < 512)
            {
                break;
            }

            var hdr = new TarHeader(hdrBuf);

            if (hdr.FileLength == 0 && string.IsNullOrEmpty(hdr.FileName))
            {
                break;
            }

            var record = new TarFileRecord(hdr, _fileStream.Position);
            if (hdr.FileType == UnixFileType.TarEntryLongLink &&
                hdr.FileName == "././@LongLink")
            {
                var buffer = ArrayPool<byte>.Shared.Rent(checked((int)hdr.FileLength));
                try
                {
                    _fileStream.ReadExactly(buffer, 0, (int)hdr.FileLength);

                    long_path = EncodingUtilities
                        .GetLatin1Encoding()
                        .GetString(TarHeader.ReadNullTerminatedString(buffer.AsSpan(0, (int)hdr.FileLength)));

                    _fileStream.Position += -(buffer.Length & 511) & 511;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            else
            {
                if (long_path is not null)
                {
                    hdr.FileName = long_path;
                    long_path = null;
                }

                _files.Add(hdr.FileName, record);
                _fileStream.Position += ((hdr.FileLength + 511) / 512) * 512;
            }
        }
    }

    /// <summary>
    /// Tries to open a file contained in the archive, if it exists.
    /// </summary>
    /// <param name="path">The path to the file within the archive.</param>
    /// <param name="stream">A stream containing the file contents, or null.</param>
    /// <returns><c>true</c> if the file could be opened, else <c>false</c>.</returns>
    public bool TryOpenFile(string path, out Stream stream)
    {
        if (_files.TryGetValue(path, out var file))
        {
            stream = new SubStream(_fileStream, file.Start, file.Length);
            return true;
        }

        stream = null;
        return false;
    }

    /// <summary>
    /// Open a file contained in the archive.
    /// </summary>
    /// <param name="path">The path to the file within the archive.</param>
    /// <returns>A stream containing the file contents.</returns>
    /// <exception cref="FileNotFoundException">Thrown if the file is not found.</exception>
    public Stream OpenFile(string path)
    {
        if (_files.TryGetValue(path, out var file))
        {
            return new SubStream(_fileStream, file.Start, file.Length);
        }

        throw new FileNotFoundException("File is not in archive", path);
    }

    public ICollection<string> GetFileNames()
    {
        return _files.Keys;
    }

    public ICollection<TarFileRecord> GetFiles()
    {
        return _files.Values;
    }

    /// <summary>
    /// Determines if a given file exists in the archive.
    /// </summary>
    /// <param name="path">The file path to test.</param>
    /// <returns><c>true</c> if the file is present, else <c>false</c>.</returns>
    public bool FileExists(string path)
    {
        return _files.ContainsKey(path);
    }

    /// <summary>
    /// Determines if a given directory exists in the archive.
    /// </summary>
    /// <param name="path">The file path to test.</param>
    /// <returns><c>true</c> if the directory is present, else <c>false</c>.</returns>
    public bool DirExists(string path)
    {
        var searchStr = path;
        searchStr = searchStr.Replace('\\', '/');
        searchStr = searchStr.EndsWith('/') ? searchStr : $"{searchStr}/";

        foreach (var filePath in _files.Keys)
        {
            if (filePath.StartsWith(searchStr, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public IEnumerable<TarFileRecord> GetFiles(string dir)
    {
        var searchStr = dir;
        searchStr = searchStr.Replace('\\', '/');
        searchStr = searchStr.EndsWith('/') ? searchStr : $"{searchStr}/";

        foreach (var filePath in _files.Keys)
        {
            if (filePath.StartsWith(searchStr, StringComparison.Ordinal))
            {
                yield return _files[filePath];
            }
        }
    }

    public static IEnumerable<TarFileData> EnumerateFiles(Stream archive)
    {
        var hdrBuf = new byte[512];

        string long_path = null;

        for (;;)
        {
            if (archive.ReadMaximum(hdrBuf, 0, 512) < 512)
            {
                break;
            }

            var hdr = new TarHeader(hdrBuf);

            if (long_path is not null)
            {
                hdr.FileName = long_path;
                long_path = null;
            }

            if (hdr.FileLength == 0 && string.IsNullOrEmpty(hdr.FileName))
            {
                break;
            }

            if (hdr.FileLength == 0)
            {
                yield return new(hdr, source: null);
            }
            else if (hdr.FileType == UnixFileType.TarEntryLongLink &&
                hdr.FileName == "././@LongLink")
            {
                var data = ArrayPool<byte>.Shared.Rent(checked((int)hdr.FileLength));
                try
                {
                    archive.ReadExactly(data, 0, (int)hdr.FileLength);

                    long_path = EncodingUtilities
                        .GetLatin1Encoding()
                        .GetString(TarHeader.ReadNullTerminatedString(data.AsSpan(0, (int)hdr.FileLength)));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(data);
                }

                var moveForward = (int)(-(hdr.FileLength & 511) & 511);

                if (archive.ReadMaximum(hdrBuf, 0, moveForward) < moveForward)
                {
                    break;
                }
            }
            else if (archive.CanSeek)
            {
                var location = archive.Position;

                var datastream = new SubStream(archive, location, hdr.FileLength);

                yield return new(hdr, datastream);

                archive.Position = location + hdr.FileLength + ((-datastream.Length) & 511);
            }
            else
            {
                Stream datastream;

                if (hdr.FileLength >= FileDatabufferChunkSize)
                {
                    var data = new SparseMemoryBuffer(FileDatabufferChunkSize);

                    if (data.WriteFromStream(0, archive, hdr.FileLength) < hdr.FileLength)
                    {
                        throw new EndOfStreamException("Unexpected end of tar stream");
                    }

                    datastream = new SparseMemoryStream(data, FileAccess.Read);
                }
                else
                {
                    var data = new byte[hdr.FileLength];

                    archive.ReadExactly(data, 0, data.Length);

                    datastream = new MemoryStream(data, writable: false);
                }

                var moveForward = (int)((-datastream.Length) & 511);

                yield return new(hdr, datastream);

                if (archive.ReadMaximum(hdrBuf, 0, moveForward) < moveForward)
                {
                    break;
                }
            }
        }
    }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
    public static async IAsyncEnumerable<TarFileData> EnumerateFilesAsync(Stream archive, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var hdrBuf = new byte[512];

        string long_path = null;

        for (; ; )
        {
            if (await archive.ReadMaximumAsync(hdrBuf.AsMemory(0, 512), cancellationToken).ConfigureAwait(false) < 512)
            {
                break;
            }

            var hdr = new TarHeader(hdrBuf);

            if (long_path is not null)
            {
                hdr.FileName = long_path;
                long_path = null;
            }

            if (hdr.FileLength == 0 && string.IsNullOrEmpty(hdr.FileName))
            {
                break;
            }

            if (hdr.FileLength == 0)
            {
                yield return new(hdr, source: null);
            }
            else if (hdr.FileType == UnixFileType.TarEntryLongLink &&
                hdr.FileName == "././@LongLink")
            {
                var data = ArrayPool<byte>.Shared.Rent(checked((int)hdr.FileLength));
                try
                {
                    await archive.ReadExactlyAsync(data.AsMemory(0, (int)hdr.FileLength), cancellationToken).ConfigureAwait(false);

                    long_path = EncodingUtilities
                        .GetLatin1Encoding()
                        .GetString(TarHeader.ReadNullTerminatedString(data.AsSpan(0, (int)hdr.FileLength)));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(data);
                }

                var moveForward = (int)(-(hdr.FileLength & 511) & 511);

                if (await archive.ReadMaximumAsync(hdrBuf.AsMemory(0, moveForward), cancellationToken).ConfigureAwait(false) < moveForward)
                {
                    break;
                }
            }
            else if (archive.CanSeek)
            {
                var location = archive.Position;

                var datastream = new SubStream(archive, location, hdr.FileLength);

                yield return new(hdr, datastream);

                archive.Position = location + hdr.FileLength + ((-datastream.Length) & 511);
            }
            else
            {
                Stream datastream;

                if (hdr.FileLength >= FileDatabufferChunkSize)
                {
                    var data = new SparseMemoryBuffer(FileDatabufferChunkSize);

                    if (await data.WriteFromStreamAsync(0, archive, hdr.FileLength, cancellationToken).ConfigureAwait(false) < hdr.FileLength)
                    {
                        throw new EndOfStreamException("Unexpected end of tar stream");
                    }

                    datastream = new SparseMemoryStream(data, FileAccess.Read);
                }
                else
                {
                    var data = new byte[hdr.FileLength];

                    await archive.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false);

                    datastream = new MemoryStream(data, writable: false);
                }

                var moveForward = (int)((-datastream.Length) & 511);

                yield return new(hdr, datastream);

                if (await archive.ReadMaximumAsync(hdrBuf.AsMemory(0, moveForward), cancellationToken).ConfigureAwait(false) < moveForward)
                {
                    break;
                }
            }
        }
    }
#endif

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
                _fileStream.Dispose();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer

            // TODO: set large fields to null
            _files.Clear();

            disposedValue = true;
        }
    }

    // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    ~TarFile()
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
