//
// Copyright (c) 2024, Olof Lagerkvist and contributors
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
using System;
using System.IO;

namespace DiscUtils.SquashFs;

/// <summary>
/// Options for the SquashFs file system reader.
/// </summary>
public sealed class SquashFileSystemReaderOptions
{
    /// <summary>
    /// Gets or sets the decompressor resolver for the file system.
    /// </summary>
    public GetDecompressorDelegate GetDecompressor { get; init; }
}

/// <summary>
/// Delegate to get a decompressor for a specific compression kind.
/// </summary>
/// <param name="compressionKind">The kind of compression</param>
/// <param name="compressionOptions">Optional option. Can be null.</param>
/// <returns>A function to decode a stream of the specified compression. Returns null if not supported.</returns>
public delegate StreamCompressorDelegate GetDecompressorDelegate(SquashFileSystemCompressionKind compressionKind, CompressionOptions compressionOptions);