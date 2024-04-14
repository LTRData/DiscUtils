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
using System.Diagnostics;

namespace DiscUtils.Diagnostics;

/// <summary>
/// A record of an individual stream activity.
/// </summary>
public sealed class StreamTraceRecord
{
    private int _id;
    private string _fileAction;
    private long _filePosition;
    private long _countArg;
    private long _result;
    private Exception _exThrown;
    private StackTrace _stack;

    internal StreamTraceRecord(int id, string fileAction, long filePosition, StackTrace stack)
    {
        _id = id;
        _fileAction = fileAction;
        _filePosition = filePosition;
        _stack = stack;
    }

    /// <summary>
    /// Unique identity for this record.
    /// </summary>
    public int Id => _id;

    /// <summary>
    /// The type of action being performed.
    /// </summary>
    public string FileAction => _fileAction;

    /// <summary>
    /// The stream position when the action was performed.
    /// </summary>
    public long FilePosition => _filePosition;

    /// <summary>
    /// The count argument (if relevant) when the action was performed.
    /// </summary>
    public long CountArg
    {
        get => _countArg;
        internal set => _countArg = value;
    }

    /// <summary>
    /// The return value (if relevant) when the action was performed.
    /// </summary>
    public long Result
    {
        get => _result;
        internal set => _result = value;
    }

    /// <summary>
    /// The exception thrown during processing of this action.
    /// </summary>
    public Exception ExceptionThrown
    {
        get => _exThrown;
        internal set => _exThrown = value;
    }

    /// <summary>
    /// A full stack trace at the point the action was performed.
    /// </summary>
    public StackTrace Stack => _stack;

    /// <summary>
    /// Gets a string representation of the common fields.
    /// </summary>
    /// <returns></returns>
    public override string ToString() =>
        $"{_id:D3}{(_exThrown != null ? "E" : " "),1}:{_fileAction,5}  @{_filePosition:X10}  [count={_countArg}, result={_result}]";
}
