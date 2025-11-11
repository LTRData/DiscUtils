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
    internal StreamTraceRecord(int id, string fileAction, long filePosition, StackTrace stack)
    {
        Id = id;
        FileAction = fileAction;
        FilePosition = filePosition;
        Stack = stack;
    }

    /// <summary>
    /// Unique identity for this record.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// The type of action being performed.
    /// </summary>
    public string FileAction { get; }

    /// <summary>
    /// The stream position when the action was performed.
    /// </summary>
    public long FilePosition { get; }

    /// <summary>
    /// The count argument (if relevant) when the action was performed.
    /// </summary>
    public long CountArg { get; internal set; }

    /// <summary>
    /// The return value (if relevant) when the action was performed.
    /// </summary>
    public long Result { get; internal set; }

    /// <summary>
    /// The exception thrown during processing of this action.
    /// </summary>
    public Exception ExceptionThrown { get; internal set; }

    /// <summary>
    /// A full stack trace at the point the action was performed.
    /// </summary>
    public StackTrace Stack { get; }

    /// <summary>
    /// Gets a string representation of the common fields.
    /// </summary>
    /// <returns></returns>
    public override string ToString() =>
        $"{Id:D3}{(ExceptionThrown != null ? "E" : " "),1}:{FileAction,5}  @{FilePosition:X10}  [count={CountArg}, result={Result}]";
}
