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
using System.IO.Compression;
using DiscUtils.Compression;

namespace DiscUtils.SquashFs;

/// <summary>
/// Options for building a SquashFs file system.
/// </summary>
public sealed class SquashFileSystemBuilderOptions
{
    /// <summary>
    /// Default options for building a SquashFs file system.
    /// </summary>
    public static SquashFileSystemBuilderOptions Default = new();


    /// <summary>
    /// Gets or sets the compression kind to use for the file system.
    /// </summary>
    public SquashFileSystemCompressionKind CompressionKind { get; init; } = SquashFileSystemCompressionKind.ZLib;

    /// <summary>
    /// Gets or sets the compression options to use for the file system. The options must be compatible with the <see cref="CompressionKind"/>. Default is null.
    /// </summary>
    public CompressionOptions CompressionOptions { get; init; }

    /// <summary>
    /// Gets or sets the compressor resolver for the file system.
    /// </summary>
    public Func<SquashFileSystemCompressionKind, CompressionOptions, StreamCompressorDelegate> GetCompressor { get; init; }

    /// <summary>
    /// Resolves the compressor for the specified compression.
    /// </summary>
    /// <returns>The compressor.</returns>
    /// <exception cref="InvalidOperationException">If no compressor was found for the specified compression.</exception>
    internal StreamCompressorDelegate ResolveCompressor()
    {
        StreamCompressorDelegate compressor = null;
        if (CompressionKind == SquashFileSystemCompressionKind.ZLib)
        {
            compressor = static stream => new ZlibStream(stream, CompressionMode.Compress, true);
        }

        if (GetCompressor != null)
        {
            compressor = GetCompressor(CompressionKind , CompressionOptions);
        }

        if (compressor == null)
        {
            throw new InvalidOperationException($"No compressor found for the specified compression {CompressionOptions}");
        }

        return compressor;
    }
}