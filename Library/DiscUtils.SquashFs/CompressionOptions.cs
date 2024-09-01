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
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.SquashFs;

/// <summary>
/// Base class for compression options.
/// </summary>
public abstract class CompressionOptions : IByteArraySerializable
{
    internal CompressionOptions(SquashFileSystemCompressionKind kind)
    {
        Kind = kind;
    }

    /// <summary>
    /// The kind of compression.
    /// </summary>
    public SquashFileSystemCompressionKind Kind { get; }


    /// <inheritdoc />
    public virtual int Size => 0;

    /// <inheritdoc />
    public virtual int ReadFrom(ReadOnlySpan<byte> buffer) => Size;

    /// <inheritdoc />
    public virtual void WriteTo(Span<byte> buffer)
    {
    }

    internal static int ConfigureCompressionAndGetTotalSize(SuperBlock block, CompressionOptions options)
    {
        if (block.Compression == SquashFileSystemCompressionKind.Lz4)
        {
            // LZ4 always has options
            block.Flags |= SuperBlock.SuperBlockFlags.CompressorOptionsPresent;
            return 8 + sizeof(ushort); // Account for the preamble ushort size
        }

        if (block.Compression == SquashFileSystemCompressionKind.Xz)
        {
            if (options != null)
            {
                block.Flags |= SuperBlock.SuperBlockFlags.CompressorOptionsPresent;
                return options.Size + sizeof(ushort); // Account for the preamble ushort size
            }
        }

        return 0;
    }

    internal static CompressionOptions ReadFrom(Stream stream, SuperBlock block)
    {
        var hasOptions = (block.Flags & SuperBlock.SuperBlockFlags.CompressorOptionsPresent) != 0;

        switch (block.Compression)
        {
            case SquashFileSystemCompressionKind.Lz4:
            {
                // LZ4 always has options
                Lz4CompressionOptions opts = new();
                var size = ReadCompressionSize(stream);
                if (size < opts.Size)
                {
                    throw new InvalidDataException($"Invalid LZ4 options size {size}. Expecting at least {opts.Size}");
                }

                opts.ReadFrom(stream, size);
                if (opts.Version != Lz4CompressionFormatVersion.Legacy)
                {
                    throw new NotSupportedException($"Unsupported LZ4 version {(uint)opts.Version}");
                }

                return new Lz4CompressionOptions();
            }

            case SquashFileSystemCompressionKind.Xz:
            {
                XzCompressionOptions opts = new(Math.Max(Metablock.SQUASHFS_METADATA_SIZE, (int)block.BlockSize));
                if (hasOptions)
                {
                    var size = ReadCompressionSize(stream);
                    if (size < opts.Size)
                    {
                        throw new InvalidDataException($"Invalid Xz options size {size}. Expecting at least {opts.Size}");
                    }

                    opts.ReadFrom(stream, size);
                }
                return opts;
            }

            case SquashFileSystemCompressionKind.ZLib:
            {
                ThrowIfHasOptions();
                return new ZLibCompressionOptions();
            }
            case SquashFileSystemCompressionKind.Lzo:
            {
                ThrowIfHasOptions();
                return new LzoCompressionOptions();
            }
            case SquashFileSystemCompressionKind.Lzma:
            {
                ThrowIfHasOptions();
                return new LzmaCompressionOptions();
            }
            case SquashFileSystemCompressionKind.ZStd:
            {
                ThrowIfHasOptions();
                return new ZStdCompressionOptions();
            }
            default:
            {
                throw new NotSupportedException($"Unsupported compression mode {(uint)block.Compression}");
            }
        }

        void ThrowIfHasOptions()
        {
            if (hasOptions)
            {
                throw new NotSupportedException($"Unsupported compression options for {block.Compression}");
            }
        }
    }

    internal static void WriteTo(Stream stream, SuperBlock block, CompressionOptions options)
    {
        if (block.Compression == SquashFileSystemCompressionKind.Lz4)
        {
            // LZ4 always has options
            options ??= new Lz4CompressionOptions();
            Span<byte> data = stackalloc byte[options.Size];
            WriteCompressionSize(stream, (ushort)options.Size);
            options.WriteTo(data);
            stream.Write(data);
        }
        else if (block.Compression == SquashFileSystemCompressionKind.Xz)
        {
            if (options != null)
            {
                Span<byte> data = stackalloc byte[options.Size];
                WriteCompressionSize(stream, (ushort)options.Size);
                options.WriteTo(data);
                stream.Write(data);
            }
        }
    }

    internal static ushort ReadCompressionSize(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[2];
        stream.ReadExactly(buffer);
        var size = EndianUtilities.ToUInt16LittleEndian(buffer);
        if ((size & Metablock.SQUASHFS_COMPRESSED_BIT) == 0)  // Flag to indicate uncompressed buffer
        {
            throw new InvalidDataException("Invalid compressed size.");
        }
        return (ushort)(size & Metablock.SQUASHFS_COMPRESSED_BIT_SIZE_MASK);
    }

    internal static void WriteCompressionSize(Stream stream, ushort size)
    {
        size |= Metablock.SQUASHFS_COMPRESSED_BIT; // Flag to indicate uncompressed buffer
        Span<byte> buffer = stackalloc byte[2];
        EndianUtilities.WriteBytesLittleEndian(size, buffer);
        stream.Write(buffer);
    }
}

/// <summary>
/// Compression options for LZ4.
/// </summary>
public class Lz4CompressionOptions : CompressionOptions
{
    private Lz4CompressionFormatVersion _version;
    private bool _highCompression;
    private const uint LZ4_HC = 1;

    /// <summary>
    /// Creates a new instance of the Lz4 compression options.
    /// </summary>
    public Lz4CompressionOptions() : base(SquashFileSystemCompressionKind.Lz4)
    {
        Version = Lz4CompressionFormatVersion.Legacy;
    }

    /// <summary>
    /// Gets or sets the version of the LZ4 compression format. Only Legacy is supported
    /// </summary>
    public Lz4CompressionFormatVersion Version
    {
        get => _version;
        init => _version = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether high compression is enabled.
    /// </summary>
    public bool HighCompression
    {
        get => _highCompression;
        init => _highCompression = value;
    }

    /// <inheritdoc />
    public override int Size => 8;

    /// <inheritdoc />
    public override int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        _version = (Lz4CompressionFormatVersion)EndianUtilities.ToInt32LittleEndian(buffer);
        // Read flags but ignore them
        _highCompression = EndianUtilities.ToInt32LittleEndian(buffer.Slice(4)) == LZ4_HC;
        return Size;
    }

    /// <inheritdoc />
    public override void WriteTo(Span<byte> buffer)
    {
        EndianUtilities.WriteBytesLittleEndian((uint)_version, buffer);
        EndianUtilities.WriteBytesLittleEndian(_highCompression ? LZ4_HC : 0, buffer.Slice(4));
    }
}

/// <summary>
/// Defines the version of the LZ4 compression format.
/// </summary>
public enum Lz4CompressionFormatVersion
{
    /// <summary>
    /// Undefined.
    /// </summary>
    Undefined = 0,

    /// <summary>
    /// The legacy version of LZ4.
    /// </summary>
    Legacy = 1,
}

/// <summary>
/// Compression options for XZ.
/// </summary>
public class XzCompressionOptions : CompressionOptions
{
    private int _dictionarySize;

    /// <summary>
    /// Creates a new instance of the XZ compression options.
    /// </summary>
    /// <param name="dictionarySize">The size of the dictionary.</param>
    public XzCompressionOptions(int dictionarySize) : base(SquashFileSystemCompressionKind.Xz)
    {
        _dictionarySize = dictionarySize;
    }

    /// <summary>
    /// Gets the size of the dictionary.
    /// </summary>
    public int DictionarySize => _dictionarySize;

    /// <inheritdoc />
    public override int Size => 8;

    /// <inheritdoc />
    public override int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        _dictionarySize = EndianUtilities.ToInt32LittleEndian(buffer);
        // Read flags but ignore them
        _ = EndianUtilities.ToInt32LittleEndian(buffer.Slice(4));
        return Size;
    }

    /// <inheritdoc />
    public override void WriteTo(Span<byte> buffer)
    {
        EndianUtilities.WriteBytesLittleEndian(_dictionarySize, buffer);
        EndianUtilities.WriteBytesLittleEndian((uint)0, buffer.Slice(4));
    }
}

/// <summary>
/// Compression options for ZLib. (No options are supported).
/// </summary>
public class ZLibCompressionOptions() : CompressionOptions(SquashFileSystemCompressionKind.ZLib);

/// <summary>
/// Compression options for LZO. (No options are supported).
/// </summary>
public class LzoCompressionOptions() : CompressionOptions(SquashFileSystemCompressionKind.Lzo);

/// <summary>
/// Compression options for LZMA. (No options are supported).
/// </summary>
public class LzmaCompressionOptions() : CompressionOptions(SquashFileSystemCompressionKind.Lzma);

/// <summary>
/// ZStandard compression options. (No options are supported).
/// </summary>
public class ZStdCompressionOptions() : CompressionOptions(SquashFileSystemCompressionKind.ZStd);