// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.IO;
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
    /// Gets or sets the compression to use for the file system.
    /// </summary>
    public SquashFileSystemCompression Compression { get; init; } = SquashFileSystemCompression.ZLib;

    /// <summary>
    /// Gets or sets the compressor resolver for the file system.
    /// </summary>
    public Func<SquashFileSystemCompression, Func<Stream, Stream>> GetCompressor { get; init; }

    /// <summary>
    /// Resolves the compressor for the specified compression.
    /// </summary>
    /// <returns>The compressor.</returns>
    /// <exception cref="InvalidOperationException">If no compressor was found for the specified compression.</exception>
    internal Func<Stream, Stream> ResolveCompressor()
    {
        Func<Stream, Stream> compressor = null;
        if (Compression == SquashFileSystemCompression.ZLib)
        {
            compressor = static stream => new ZlibStream(stream, CompressionMode.Compress, true);
        }

        if (GetCompressor != null)
        {
            compressor = GetCompressor(Compression);
        }

        if (compressor == null)
        {
            throw new InvalidOperationException($"No compressor found for the specified compression {Compression}");
        }

        return compressor;
    }
}