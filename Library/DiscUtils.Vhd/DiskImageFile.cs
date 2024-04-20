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
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using DiscUtils.Internal;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Vhd;

/// <summary>
/// Represents a single .VHD file.
/// </summary>
public sealed class DiskImageFile : VirtualDiskLayer
{
    /// <summary>
    /// The VHD file's dynamic header (if not static).
    /// </summary>
    private DynamicHeader _dynamicHeader;

    /// <summary>
    /// The object that can be used to locate relative file paths.
    /// </summary>
    private readonly FileLocator _fileLocator;

    /// <summary>
    /// The file name of this VHD.
    /// </summary>
    private readonly string _fileName;

    /// <summary>
    /// The stream containing the VHD file.
    /// </summary>
    private Stream _fileStream;

    /// <summary>
    /// The VHD file's footer.
    /// </summary>
    private Footer _footer;

    /// <summary>
    /// Indicates if this object controls the lifetime of the stream.
    /// </summary>
    private readonly Ownership _ownsStream;

    /// <summary>
    /// Initializes a new instance of the DiskImageFile class.
    /// </summary>
    /// <param name="stream">The stream to interpret.</param>
    public DiskImageFile(Stream stream)
    {
        _fileStream = stream;

        ReadFooter(true);

        ReadHeaders();
    }

    /// <summary>
    /// Initializes a new instance of the DiskImageFile class.
    /// </summary>
    /// <param name="stream">The stream to interpret.</param>
    /// <param name="ownsStream">Indicates if the new instance should control the lifetime of the stream.</param>
    public DiskImageFile(Stream stream, Ownership ownsStream)
    {
        _fileStream = stream;
        _ownsStream = ownsStream;

        ReadFooter(true);

        ReadHeaders();
    }

    /// <summary>
    /// Initializes a new instance of the DiskImageFile class.
    /// </summary>
    /// <param name="path">The file path to open.</param>
    /// <param name="access">Controls how the file can be accessed.</param>
    /// <param name="useAsync">Underlying files will be opened optimized for async use.</param>
    public DiskImageFile(string path, FileAccess access, bool useAsync = false)
        : this(new LocalFileLocator(Path.GetDirectoryName(path), useAsync), Path.GetFileName(path), access) {}

    internal DiskImageFile(FileLocator locator, string path, Stream stream, Ownership ownsStream)
        : this(stream, ownsStream)
    {
        _fileLocator = locator.GetRelativeLocator(locator.GetDirectoryFromPath(path));
        _fileName = locator.GetFileFromPath(path);
    }

    internal DiskImageFile(FileLocator locator, string path, FileAccess access)
    {
        var share = access == FileAccess.Read ? FileShare.Read : FileShare.None;
        _fileStream = locator.Open(path, FileMode.Open, access, share);
        _ownsStream = Ownership.Dispose;

        try
        {
            _fileLocator = locator.GetRelativeLocator(locator.GetDirectoryFromPath(path));
            _fileName = locator.GetFileFromPath(path);

            ReadFooter(true);

            ReadHeaders();
        }
        catch
        {
            _fileStream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the layer data is opened for writing.
    /// </summary>
    public override bool CanWrite => _fileStream.CanWrite;

    public override long Capacity => _footer.CurrentSize;

    /// <summary>
    /// Gets the timestamp for this file (when it was created).
    /// </summary>
    public DateTime CreationTimestamp => _footer.Timestamp;

    /// <summary>
    /// Gets the extent that comprises this file.
    /// </summary>
    public override IList<VirtualDiskExtent> Extents
    {
        get
        {
            var result = new List<VirtualDiskExtent>
            {
                new DiskExtent(this)
            };
            return result;
        }
    }

    /// <summary>
    /// Gets the full path to this disk layer, or empty string.
    /// </summary>
    public override string FullPath
    {
        get
        {
            if (_fileLocator != null && _fileName != null)
            {
                return _fileLocator.GetFullPath(_fileName);
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// Gets the geometry of the virtual disk.
    /// </summary>
    public override Geometry Geometry => _footer.Geometry;

    /// <summary>
    /// Gets detailed information about the VHD file.
    /// </summary>
    public DiskImageFileInfo Information => new(_footer, _dynamicHeader, _fileStream);

    /// <summary>
    /// Gets a value indicating if the layer only stores meaningful sectors.
    /// </summary>
    public override bool IsSparse => _footer.DiskType != FileType.Fixed;

    /// <summary>
    /// Gets a value indicating whether the file is a differencing disk.
    /// </summary>
    public override bool NeedsParent => _footer.DiskType == FileType.Differencing;

    /// <summary>
    /// Gets the unique id of the parent disk.
    /// </summary>
    public Guid ParentUniqueId => _dynamicHeader == null ? Guid.Empty : _dynamicHeader.ParentUniqueId;

    public override FileLocator RelativeFileLocator => _fileLocator;

    internal long StoredSize => _fileStream.Length;

    /// <summary>
    /// Gets the unique id of this file.
    /// </summary>
    public Guid UniqueId => _footer.UniqueId;

    /// <summary>
    /// Initializes a stream as a fixed-sized VHD file.
    /// </summary>
    /// <param name="stream">The stream to initialize.</param>
    /// <param name="ownsStream">Indicates if the new instance controls the lifetime of the stream.</param>
    /// <param name="capacity">The desired capacity of the new disk.</param>
    /// <returns>An object that accesses the stream as a VHD file.</returns>
    public static DiskImageFile InitializeFixed(Stream stream, Ownership ownsStream, long capacity)
    {
        return InitializeFixed(stream, ownsStream, capacity, null);
    }

    /// <summary>
    /// Initializes a stream as a fixed-sized VHD file.
    /// </summary>
    /// <param name="stream">The stream to initialize.</param>
    /// <param name="ownsStream">Indicates if the new instance controls the lifetime of the stream.</param>
    /// <param name="capacity">The desired capacity of the new disk.</param>
    /// <param name="geometry">The desired geometry of the new disk, or <c>null</c> for default.</param>
    /// <returns>An object that accesses the stream as a VHD file.</returns>
    public static DiskImageFile InitializeFixed(Stream stream, Ownership ownsStream, long capacity,
                                                Geometry? geometry)
    {
        InitializeFixedInternal(stream, capacity, geometry);
        return new DiskImageFile(stream, ownsStream);
    }

    /// <summary>
    /// Initializes a stream as a dynamically-sized VHD file.
    /// </summary>
    /// <param name="stream">The stream to initialize.</param>
    /// <param name="ownsStream">Indicates if the new instance controls the lifetime of the stream.</param>
    /// <param name="capacity">The desired capacity of the new disk.</param>
    /// <returns>An object that accesses the stream as a VHD file.</returns>
    public static DiskImageFile InitializeDynamic(Stream stream, Ownership ownsStream, long capacity)
    {
        return InitializeDynamic(stream, ownsStream, capacity, null, DynamicHeader.DefaultBlockSize);
    }

    /// <summary>
    /// Initializes a stream as a dynamically-sized VHD file.
    /// </summary>
    /// <param name="stream">The stream to initialize.</param>
    /// <param name="ownsStream">Indicates if the new instance controls the lifetime of the stream.</param>
    /// <param name="capacity">The desired capacity of the new disk.</param>
    /// <param name="geometry">The desired geometry of the new disk, or <c>null</c> for default.</param>
    /// <returns>An object that accesses the stream as a VHD file.</returns>
    public static DiskImageFile InitializeDynamic(Stream stream, Ownership ownsStream, long capacity,
                                                  Geometry? geometry)
    {
        return InitializeDynamic(stream, ownsStream, capacity, geometry, DynamicHeader.DefaultBlockSize);
    }

    /// <summary>
    /// Initializes a stream as a dynamically-sized VHD file.
    /// </summary>
    /// <param name="stream">The stream to initialize.</param>
    /// <param name="ownsStream">Indicates if the new instance controls the lifetime of the stream.</param>
    /// <param name="capacity">The desired capacity of the new disk.</param>
    /// <param name="blockSize">The size of each block (unit of allocation).</param>
    /// <returns>An object that accesses the stream as a VHD file.</returns>
    public static DiskImageFile InitializeDynamic(Stream stream, Ownership ownsStream, long capacity, long blockSize)
    {
        return InitializeDynamic(stream, ownsStream, capacity, null, blockSize);
    }

    /// <summary>
    /// Initializes a stream as a dynamically-sized VHD file.
    /// </summary>
    /// <param name="stream">The stream to initialize.</param>
    /// <param name="ownsStream">Indicates if the new instance controls the lifetime of the stream.</param>
    /// <param name="capacity">The desired capacity of the new disk.</param>
    /// <param name="geometry">The desired geometry of the new disk, or <c>null</c> for default.</param>
    /// <param name="blockSize">The size of each block (unit of allocation).</param>
    /// <returns>An object that accesses the stream as a VHD file.</returns>
    public static DiskImageFile InitializeDynamic(Stream stream, Ownership ownsStream, long capacity,
                                                  Geometry? geometry, long blockSize)
    {
        InitializeDynamicInternal(stream, capacity, geometry, blockSize);

        return new DiskImageFile(stream, ownsStream);
    }

    /// <summary>
    /// Initializes a stream as a differencing disk VHD file.
    /// </summary>
    /// <param name="stream">The stream to initialize.</param>
    /// <param name="ownsStream">Indicates if the new instance controls the lifetime of the stream.</param>
    /// <param name="parent">The disk this file is a different from.</param>
    /// <param name="parentAbsolutePath">The full path to the parent disk.</param>
    /// <param name="parentRelativePath">The relative path from the new disk to the parent disk.</param>
    /// <param name="parentModificationTimeUtc">The time the parent disk's file was last modified (from file system).</param>
    /// <returns>An object that accesses the stream as a VHD file.</returns>
    public static DiskImageFile InitializeDifferencing(
        Stream stream,
        Ownership ownsStream,
        DiskImageFile parent,
        string parentAbsolutePath,
        string parentRelativePath,
        DateTime parentModificationTimeUtc)
    {
        InitializeDifferencingInternal(stream, parent, parentAbsolutePath, parentRelativePath,
            parentModificationTimeUtc);

        return new DiskImageFile(stream, ownsStream);
    }

    /// <summary>
    /// Opens the content of the disk image file as a stream.
    /// </summary>
    /// <param name="parent">The parent file's content (if any).</param>
    /// <param name="ownsParent">Whether the created stream assumes ownership of parent stream.</param>
    /// <returns>The new content stream.</returns>
    public override SparseStream OpenContent(SparseStream parent, Ownership ownsParent)
    {
        return DoOpenContent(parent, ownsParent);
    }

    /// <summary>
    /// Gets the location of the parent file, given a base path.
    /// </summary>
    /// <returns>Array of candidate file locations.</returns>
    public override IEnumerable<string> GetParentLocations()
    {
        return GetParentLocations(_fileLocator);
    }

    /// <summary>
    /// Gets the location of the parent file, given a base path.
    /// </summary>
    /// <param name="basePath">The full path to this file.</param>
    /// <returns>Array of candidate file locations.</returns>
    [Obsolete("Use GetParentLocations() by preference")]
    public string[] GetParentLocations(string basePath)
    {
        return GetParentLocations(new LocalFileLocator(basePath, useAsync: false)).ToArray();
    }

    internal static DiskImageFile InitializeFixed(FileLocator locator, string path, long capacity, Geometry? geometry)
    {
        DiskImageFile result = null;
        var stream = locator.Open(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        try
        {
            InitializeFixedInternal(stream, capacity, geometry);
            result = new DiskImageFile(locator, path, stream, Ownership.Dispose);
            stream = null;
        }
        finally
        {
            stream?.Dispose();
        }

        return result;
    }

    internal static DiskImageFile InitializeDynamic(FileLocator locator, string path, long capacity,
                                                    Geometry? geometry, long blockSize)
    {
        DiskImageFile result = null;
        var stream = locator.Open(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        try
        {
            InitializeDynamicInternal(stream, capacity, geometry, blockSize);
            result = new DiskImageFile(locator, path, stream, Ownership.Dispose);
            stream = null;
        }
        finally
        {
            stream?.Dispose();
        }

        return result;
    }

    internal DiskImageFile CreateDifferencing(FileLocator fileLocator, string path)
    {
        var stream = fileLocator.Open(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var fullPath = _fileLocator.GetFullPath(_fileName);
        var relativePath = fileLocator.MakeRelativePath(_fileLocator, _fileName);
        var lastWriteTime = _fileLocator.GetLastWriteTimeUtc(_fileName);

        InitializeDifferencingInternal(stream, this, fullPath, relativePath, lastWriteTime);

        return new DiskImageFile(fileLocator, path, stream, Ownership.Dispose);
    }

    internal MappedStream DoOpenContent(SparseStream parent, Ownership ownsParent)
    {
        if (_footer.DiskType == FileType.Fixed)
        {
            if (parent != null && ownsParent == Ownership.Dispose)
            {
                parent.Dispose();
            }

            return new SubStream(_fileStream, 0, _fileStream.Length - 512);
        }

        if (_footer.DiskType == FileType.Dynamic)
        {
            if (parent != null && ownsParent == Ownership.Dispose)
            {
                parent.Dispose();
            }

            return new DynamicStream(_fileStream, _dynamicHeader, _footer.CurrentSize,
                new ZeroStream(_footer.CurrentSize), Ownership.Dispose);
        }

        if (parent == null)
        {
            parent = new ZeroStream(_footer.CurrentSize);
            ownsParent = Ownership.Dispose;
        }

        return new DynamicStream(_fileStream, _dynamicHeader, _footer.CurrentSize, parent, ownsParent);
    }

    /// <summary>
    /// Disposes of underlying resources.
    /// </summary>
    /// <param name="disposing">Set to <c>true</c> if called within Dispose(),
    /// else <c>false</c>.</param>
    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing)
            {
                if (_ownsStream == Ownership.Dispose && _fileStream != null)
                {
                    _fileStream.Dispose();
                }

                _fileStream = null;
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }

    private static void InitializeFixedInternal(Stream stream, long capacity, Geometry? geometry)
    {
        geometry ??= DiscUtils.Geometry.FromCapacity(capacity);

        var footer = new Footer(geometry.Value, capacity, FileType.Fixed);
        footer.UpdateChecksum();

        Span<byte> sector = stackalloc byte[Sizes.Sector];
        footer.ToBytes(sector);
        stream.Position = MathUtilities.RoundUp(capacity, Sizes.Sector);
        stream.Write(sector);
        stream.SetLength(stream.Position);

        stream.Position = 0;
    }

    private static void InitializeDynamicInternal(Stream stream, long capacity, Geometry? geometry, long blockSize)
    {
        if (blockSize is > uint.MaxValue or < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(blockSize), "Must be in the range 0 to uint.MaxValue");
        }

        geometry ??= DiscUtils.Geometry.FromCapacity(capacity);

        var footer = new Footer(geometry.Value, capacity, FileType.Dynamic)
        {
            DataOffset = 512 // Offset of Dynamic Header
        };
        footer.UpdateChecksum();
        Span<byte> footerBlock = stackalloc byte[512];
        footer.ToBytes(footerBlock);

        var dynamicHeader = new DynamicHeader(-1, 1024 + 512, (uint)blockSize, capacity);
        dynamicHeader.UpdateChecksum();
        Span<byte> dynamicHeaderBlock = stackalloc byte[1024];
        dynamicHeader.ToBytes(dynamicHeaderBlock);

        var batSize = (dynamicHeader.MaxTableEntries * 4 + Sizes.Sector - 1) / Sizes.Sector *
                      Sizes.Sector;

        var bat = batSize <= 1024 ? stackalloc byte[batSize] : new byte[batSize];

        bat.Fill(0xff);

        stream.Position = 0;
        stream.Write(footerBlock);
        stream.Write(dynamicHeaderBlock);
        stream.Write(bat);
        stream.Write(footerBlock);
    }

    private static void InitializeDifferencingInternal(Stream stream, DiskImageFile parent,
                                                       string parentAbsolutePath, string parentRelativePath, DateTime parentModificationTimeUtc)
    {
        var footer = new Footer(parent.Geometry, parent._footer.CurrentSize, FileType.Differencing)
        {
            DataOffset = 512, // Offset of Dynamic Header
            OriginalSize = parent._footer.OriginalSize
        };
        footer.UpdateChecksum();
        Span<byte> footerBlock = stackalloc byte[512];
        footer.ToBytes(footerBlock);

        long tableOffset = 512 + 1024; // Footer + Header

        var blockSize = parent._dynamicHeader == null
            ? DynamicHeader.DefaultBlockSize
            : parent._dynamicHeader.BlockSize;

        var dynamicHeader = new DynamicHeader(-1, tableOffset, blockSize, footer.CurrentSize);
        var batSize = (dynamicHeader.MaxTableEntries * 4 + Sizes.Sector - 1) / Sizes.Sector *
                      Sizes.Sector;
        dynamicHeader.ParentUniqueId = parent.UniqueId;
        dynamicHeader.ParentTimestamp = parentModificationTimeUtc;
        dynamicHeader.ParentUnicodeName = Utilities.GetFileFromPath(parentAbsolutePath);
        dynamicHeader.ParentLocators[7].PlatformCode = ParentLocator.PlatformCodeWindowsAbsoluteUnicode;
        dynamicHeader.ParentLocators[7].PlatformDataSpace = 512;
        dynamicHeader.ParentLocators[7].PlatformDataLength = parentAbsolutePath.Length * 2;
        dynamicHeader.ParentLocators[7].PlatformDataOffset = tableOffset + batSize;
        dynamicHeader.ParentLocators[6].PlatformCode = ParentLocator.PlatformCodeWindowsRelativeUnicode;
        dynamicHeader.ParentLocators[6].PlatformDataSpace = 512;
        dynamicHeader.ParentLocators[6].PlatformDataLength = parentRelativePath.Length * 2;
        dynamicHeader.ParentLocators[6].PlatformDataOffset = tableOffset + batSize + 512;
        dynamicHeader.UpdateChecksum();
        Span<byte> dynamicHeaderBlock = stackalloc byte[1024];
        dynamicHeader.ToBytes(dynamicHeaderBlock);

        Span<byte> platformLocator1 = stackalloc byte[512];
        Encoding.Unicode.GetBytes(parentAbsolutePath.AsSpan(), platformLocator1);
        Span<byte> platformLocator2 = stackalloc byte[512];
        Encoding.Unicode.GetBytes(parentRelativePath.AsSpan(), platformLocator2);

        Span<byte> bat = batSize <= 1024 ? stackalloc byte[batSize] : new byte[batSize];

        bat.Fill(0xff);

        stream.Position = 0;
        stream.Write(footerBlock);
        stream.Write(dynamicHeaderBlock);
        stream.Write(bat);
        stream.Write(platformLocator1);
        stream.Write(platformLocator2);
        stream.Write(footerBlock);
    }

    /// <summary>
    /// Gets the locations of the parent file.
    /// </summary>
    /// <param name="fileLocator">The file locator to use.</param>
    /// <returns>Array of candidate file locations.</returns>
    private ReadOnlyCollection<string> GetParentLocations(FileLocator fileLocator)
    {
        if (!NeedsParent)
        {
            throw new InvalidOperationException("Only differencing disks contain parent locations");
        }

        // Use working directory by default
        fileLocator ??= new LocalFileLocator(string.Empty, useAsync: false);

        var absPaths = new List<string>(8);
        var relPaths = new List<string>(8);
        foreach (var pl in _dynamicHeader.ParentLocators)
        {
            if (pl.PlatformCode is ParentLocator.PlatformCodeWindowsAbsoluteUnicode
                or ParentLocator.PlatformCodeWindowsRelativeUnicode)
            {
                _fileStream.Position = pl.PlatformDataOffset;
                var buffer = _fileStream.ReadExactly(pl.PlatformDataLength);
                var locationVal = Encoding.Unicode.GetString(buffer);

                if (pl.PlatformCode == ParentLocator.PlatformCodeWindowsAbsoluteUnicode)
                {
                    absPaths.Add(locationVal);
                }
                else
                {
                    relPaths.Add(fileLocator.ResolveRelativePath(locationVal.Replace('\\', Path.DirectorySeparatorChar)));
                }
            }
        }

        // Order the paths to put absolute paths first
        var paths = new List<string>(absPaths.Count + relPaths.Count + 1);
        paths.AddRange(absPaths);
        paths.AddRange(relPaths);

        // As a back-up, try to infer from the parent name...
        if (paths.Count == 0)
        {
            paths.Add(fileLocator.ResolveRelativePath(_dynamicHeader.ParentUnicodeName));
        }

        return paths.AsReadOnly();
    }

    private void ReadFooter(bool fallbackToFront)
    {
        _fileStream.Position = _fileStream.Length - Sizes.Sector;
        Span<byte> sector = stackalloc byte[Sizes.Sector];
        _fileStream.ReadExactly(sector);

        _footer = Footer.FromBytes(sector);

        if (!_footer.IsValid())
        {
            if (!fallbackToFront)
            {
                throw new IOException("Corrupt VHD file - invalid footer at end (did not check front of file)");
            }

            _fileStream.Position = 0;
            _fileStream.ReadExactly(sector);

            _footer = Footer.FromBytes(sector);
            if (!_footer.IsValid())
            {
                throw new IOException(
                    "Failed to find a valid VHD footer at start or end of file - VHD file is corrupt");
            }
        }
    }

    private void ReadHeaders()
    {
        var pos = _footer.DataOffset;
        while (pos != -1)
        {
            _fileStream.Position = pos;
            var hdr = Header.FromStream(_fileStream);
            if (hdr.Cookie == DynamicHeader.HeaderCookie)
            {
                _fileStream.Position = pos;
                _dynamicHeader = DynamicHeader.FromStream(_fileStream);
                if (!_dynamicHeader.IsValid())
                {
                    throw new IOException("Invalid Dynamic Disc Header");
                }
            }

            pos = hdr.DataOffset;
        }
    }
}