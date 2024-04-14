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
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Internal;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;
using LTRData.Extensions.Async;

namespace DiscUtils.Diagnostics;

/// <summary>
/// Stream wrapper that traces all read and write activity.
/// </summary>
public sealed class TracingStream : CompatibilityStream
{
    private Stream _wrapped;
    private Ownership _ownsWrapped;

    private List<StreamTraceRecord> _records;
    private bool _active;
    private bool _captureStack;
    private bool _captureStackFileDetails = false;
    private bool _traceReads;
    private bool _traceWrites = true;

    private StreamWriter _fileOut;

    /// <summary>
    /// Creates a new instance, wrapping an existing stream.
    /// </summary>
    /// <param name="toWrap">The stream to wrap</param>
    /// <param name="ownsWrapped">Indicates if this stream controls toWrap's lifetime</param>
    public TracingStream(Stream toWrap, Ownership ownsWrapped)
    {
        _wrapped = toWrap;
        _ownsWrapped = ownsWrapped;
        _records = new List<StreamTraceRecord>();
    }

    /// <summary>
    /// Disposes of this instance.
    /// </summary>
    /// <param name="disposing"><c>true</c> if called from Dispose(), else <c>false</c></param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_ownsWrapped == Ownership.Dispose && _wrapped != null)
            {
                _wrapped.Dispose();
            }

            _wrapped = null;

            _fileOut?.Dispose();

            _fileOut = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Starts tracing stream activity.
    /// </summary>
    public void Start()
    {
        _active = true;
    }

    /// <summary>
    /// Stops tracing stream activity.
    /// </summary>
    /// <remarks>Old trace records are not discarded, use <c>Start</c> to resume the trace</remarks>
    public void Stop()
    {
        _active = false;
    }

    /// <summary>
    /// Resets tracing on the stream.
    /// </summary>
    /// <param name="start">Whether to enable or disable tracing after this method completes</param>
    public void Reset(bool start)
    {
        _active = false;
        _records.Clear();
        if (start)
        {
            Start();
        }
    }

    /// <summary>
    /// Gets and sets whether to capture stack traces for every read/write
    /// </summary>
    public bool CaptureStackTraces
    {
        get => _captureStack;
        set => _captureStack = value;
    }

    /// <summary>
    /// Gets and sets whether to trace read activity (default is false).
    /// </summary>
    public bool TraceReads
    {
        get => _traceReads;
        set => _traceReads = value;
    }

    /// <summary>
    /// Gets and sets whether to trace write activity (default is true).
    /// </summary>
    public bool TraceWrites
    {
        get => _traceWrites;
        set => _traceWrites = value;
    }

    /// <summary>
    /// Directs trace output to a file as well as storing internally.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <remarks>Call this method after tracing has started to migrate to a new
    /// output file.</remarks>
    public void WriteToFile(string path)
    {
        if (_fileOut != null)
        {
            _fileOut.Dispose();
            _fileOut = null;
        }

        if (!string.IsNullOrEmpty(path))
        {
            var locator = new LocalFileLocator(string.Empty, useAsync: false);
            _fileOut = new StreamWriter(locator.Open(path, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite));
        }
    }

    /// <summary>
    /// Gets a log of all recorded stream activity.
    /// </summary>
    public IEnumerable<StreamTraceRecord> Log => _records;

    /// <summary>
    /// Gets an indication as to whether the stream can be read.
    /// </summary>
    public override bool CanRead => _wrapped.CanRead;

    /// <summary>
    /// Gets an indication as to whether the stream position can be changed.
    /// </summary>
    public override bool CanSeek => _wrapped.CanSeek;

    /// <summary>
    /// Gets an indication as to whether the stream can be written to.
    /// </summary>
    public override bool CanWrite => _wrapped.CanWrite;

    /// <summary>
    /// Flushes the stream.
    /// </summary>
    public override void Flush()
    {
        _wrapped.Flush();
    }

    /// <summary>
    /// Gets the length of the stream.
    /// </summary>
    public override long Length => _wrapped.Length;

    /// <summary>
    /// Gets and sets the current stream position.
    /// </summary>
    public override long Position
    {
        get => _wrapped.Position;
        set => _wrapped.Position = value;
    }

    /// <summary>
    /// Reads data from the stream.
    /// </summary>
    /// <param name="buffer">The buffer to fill</param>
    /// <param name="offset">The buffer offset to start from</param>
    /// <param name="count">The number of bytes to read</param>
    /// <returns>The number of bytes read</returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        var position = _wrapped.Position;
        try
        {
            var result = _wrapped.Read(buffer, offset, count);
            if (_active && _traceReads)
            {
                CreateAndAddRecord("READ", position, count, result);
            }

            return result;
        }
        catch (Exception e)
        {
            if (_active && _traceReads)
            {
                CreateAndAddRecord("READ", position, count, e);
            }

            throw;
        }
    }

    /// <summary>
    /// Reads data from the stream.
    /// </summary>
    /// <param name="buffer">The buffer to fill</param>
    /// <returns>The number of bytes read</returns>
    public override int Read(Span<byte> buffer)
    {
        var position = _wrapped.Position;
        try
        {
            var result = _wrapped.Read(buffer);
            if (_active && _traceReads)
            {
                CreateAndAddRecord("READ", position, buffer.Length, result);
            }

            return result;
        }
        catch (Exception e)
        {
            if (_active && _traceReads)
            {
                CreateAndAddRecord("READ", position, buffer.Length, e);
            }

            throw;
        }
    }

    /// <summary>
    /// Reads data from the stream.
    /// </summary>
    /// <param name="buffer">The buffer to fill</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The number of bytes read</returns>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var position = _wrapped.Position;
        try
        {
            var result = await _wrapped.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (_active && _traceReads)
            {
                CreateAndAddRecord("READ", position, buffer.Length, result);
            }

            return result;
        }
        catch (Exception e)
        {
            if (_active && _traceReads)
            {
                CreateAndAddRecord("READ", position, buffer.Length, e);
            }

            throw;
        }
    }

    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state) =>
        ReadAsync(buffer, offset, count, CancellationToken.None).AsAsyncResult(callback, state);

    public override int EndRead(IAsyncResult asyncResult) => ((Task<int>)asyncResult).GetAwaiter().GetResult();

    public async override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var position = _wrapped.Position;
        try
        {
            var result = await _wrapped.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            if (_active && _traceReads)
            {
                CreateAndAddRecord("READ", position, count, result);
            }

            return result;
        }
        catch (Exception e)
        {
            if (_active && _traceReads)
            {
                CreateAndAddRecord("READ", position, count, e);
            }

            throw;
        }
    }

    /// <summary>
    /// Moves the stream position.
    /// </summary>
    /// <param name="offset">The origin-relative location</param>
    /// <param name="origin">The base location</param>
    /// <returns>The new absolute stream position</returns>
    public override long Seek(long offset, SeekOrigin origin)
    {
        return _wrapped.Seek(offset, origin);
    }

    /// <summary>
    /// Sets the length of the stream.
    /// </summary>
    /// <param name="value">The new length</param>
    public override void SetLength(long value)
    {
        _wrapped.SetLength(value);
    }

    /// <summary>
    /// Writes data to the stream at the current location.
    /// </summary>
    /// <param name="buffer">The data to write</param>
    /// <param name="offset">The first byte to write from buffer</param>
    /// <param name="count">The number of bytes to write</param>
    public override void Write(byte[] buffer, int offset, int count)
    {
        var position = _wrapped.Position;
        try
        {
            _wrapped.Write(buffer, offset, count);
            if (_active && _traceWrites)
            {
                CreateAndAddRecord("WRITE", position, count);
            }
        }
        catch (Exception e)
        {
            if (_active && _traceWrites)
            {
                CreateAndAddRecord("WRITE", position, count, e);
            }

            throw;
        }
    }

    /// <summary>
    /// Writes data to the stream at the current location.
    /// </summary>
    /// <param name="buffer">The data to write</param>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        var position = _wrapped.Position;
        try
        {
            _wrapped.Write(buffer);
            if (_active && _traceWrites)
            {
                CreateAndAddRecord("WRITE", position, buffer.Length);
            }
        }
        catch (Exception e)
        {
            if (_active && _traceWrites)
            {
                CreateAndAddRecord("WRITE", position, buffer.Length, e);
            }

            throw;
        }
    }

    /// <summary>
    /// Writes data to the stream at the current location.
    /// </summary>
    /// <param name="buffer">The data to write</param>
    /// <param name="cancellationToken"></param>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var position = _wrapped.Position;
        try
        {
            await _wrapped.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (_active && _traceWrites)
            {
                CreateAndAddRecord("WRITE", position, buffer.Length);
            }
        }
        catch (Exception e)
        {
            if (_active && _traceWrites)
            {
                CreateAndAddRecord("WRITE", position, buffer.Length, e);
            }

            throw;
        }
    }

    private StreamTraceRecord CreateAndAddRecord(string activity, long position, long count)
    {
        return CreateAndAddRecord(activity, position, count, 0, null);
    }

    private StreamTraceRecord CreateAndAddRecord(string activity, long position, long count, int result)
    {
        return CreateAndAddRecord(activity, position, count, result, null);
    }

    private StreamTraceRecord CreateAndAddRecord(string activity, long position, long count, Exception e)
    {
        return CreateAndAddRecord(activity, position, count, -1, e);
    }

    private StreamTraceRecord CreateAndAddRecord(string activity, long position, long count, int result, Exception ex)
    {
        // var trace = (_captureStack ? new StackTrace(2, _captureStackFileDetails) : null);
        // Note: Not sure about the 'ex' parameter to StackTrace, but the new StackTrace does not accept a frameCount
        var trace = (_captureStack ? new StackTrace(ex, _captureStackFileDetails) : null);
        var record = new StreamTraceRecord(_records.Count, activity, position, trace)
        {
            CountArg = count,
            Result = result,
            ExceptionThrown = ex
        };
        _records.Add(record);

        if (_fileOut != null)
        {
            _fileOut.WriteLine(record);
            if (trace != null)
            {
                _fileOut.Write(trace.ToString());
            }

            if (ex != null)
            {
                _fileOut.WriteLine(ex);
            }

            _fileOut.Flush();
        }

        return record;
    }
}
