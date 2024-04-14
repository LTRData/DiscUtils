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
using System.IO;
using System.Linq;
using System.Text;
using DiscUtils.Internal;
using DiscUtils.Streams;
using LTRData.Extensions.Buffers;
using LTRData.Extensions.Split;

namespace DiscUtils.Iso9660;

/// <summary>
/// Class that creates ISO images.
/// </summary>
/// <example>
/// <code>
///   CDBuilder builder = new CDBuilder();
///   builder.VolumeIdentifier = "MYISO";
///   builder.UseJoliet = true;
///   builder.AddFile("Hello.txt", Encoding.ASCII.GetBytes("hello world!"));
///   builder.Build(@"C:\TEMP\myiso.iso");
/// </code>
/// </example>
public sealed class CDBuilder : StreamBuilder, IFileSystemBuilder
{
    private const long DiskStart = 0x8000;
    private BootInitialEntry _bootEntry;
    private Stream _bootImage;

    private readonly BuildParameters _buildParams;
    private readonly List<BuildDirectoryInfo> _dirs;

    private readonly List<BuildFileInfo> _files;
    private readonly BuildDirectoryInfo _rootDirectory;

    /// <summary>
    /// Initializes a new instance of the CDBuilder class.
    /// </summary>
    public CDBuilder()
    {
        _files = [];
        _dirs = [];
        _rootDirectory = new BuildDirectoryInfo("\0", null);
        _dirs.Add(_rootDirectory);

        _buildParams = new BuildParameters
        {
            UseJoliet = true
        };
    }

    public bool TrackEqualSourceFiles { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to update the ISOLINUX info table at the
    /// start of the boot image.  Use with ISOLINUX only.
    /// </summary>
    /// <remarks>
    /// ISOLINUX has an 'information table' at the start of the boot loader that verifies
    /// the CD has been loaded correctly by the BIOS.  This table needs to be updated
    /// to match the actual ISO.
    /// </remarks>
    public bool UpdateIsolinuxBootTable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Joliet file-system extensions should be used.
    /// </summary>
    public bool UseJoliet
    {
        get => _buildParams.UseJoliet;
        set => _buildParams.UseJoliet = value;
    }

    /// <summary>
    /// Gets or sets the Volume Identifier for the ISO file.
    /// </summary>
    /// <remarks>
    /// Must be a valid identifier, i.e. max 32 characters in the range A-Z, 0-9 or _.
    /// Lower-case characters are not permitted.
    /// </remarks>
    public string VolumeIdentifier
    {
        get => _buildParams.VolumeIdentifier;

        set
        {
            if (value.Length > 32)
            {
                throw new ArgumentException("Not a valid volume identifier");
            }

            _buildParams.VolumeIdentifier = value;
        }
    }

    /// <summary>
    /// Gets or sets the Manufacturer ID for the ISO file.
    /// </summary>
    /// <remarks>
    /// Must be a valid identifier, i.e. max 23 characters.
    /// </remarks>
    public string ManufacturerId
    {
        get => _buildParams.ManufacturerId;

        set
        {
            if (value.Length > 23)
            {
                throw new ArgumentException("Not a valid volume identifier");
            }

            _buildParams.ManufacturerId = value;
        }
    }

    /// <summary>
    /// Sets the boot image for the ISO image.
    /// </summary>
    /// <param name="image">Stream containing the boot image.</param>
    /// <param name="emulation">The type of emulation requested of the BIOS.</param>
    /// <param name="loadSegment">The memory segment to load the image to (0 for default).</param>
    public void SetBootImage(Stream image, BootDeviceEmulation emulation, int loadSegment)
    {
        if (_bootEntry != null)
        {
            throw new InvalidOperationException("Boot image already set");
        }

        _bootEntry = new BootInitialEntry
        {
            BootIndicator = 0x88,
            BootMediaType = emulation,
            LoadSegment = (ushort)loadSegment,
            SystemType = 0
        };
        _bootImage = image;
    }

    /// <summary>
    /// Adds a directory to the ISO image.
    /// </summary>
    /// <param name="name">The name of the directory on the ISO image.</param>
    /// <returns>The object representing this directory.</returns>
    /// <remarks>
    /// The name is the full path to the directory, for example:
    /// <example><code>
    ///   builder.AddDirectory(@"DIRA\DIRB\DIRC");
    /// </code></example>
    /// </remarks>
    public BuildDirectoryInfo AddDirectory(string name)
    {
        var nameElements = name.AsMemory().Split('\\', '/', StringSplitOptions.RemoveEmptyEntries).ToArray();
        return GetDirectory(nameElements, nameElements.Length, true);
    }

    private void AddFile(BuildFileInfo fi)
    {
        _files.Add(fi);
    }

    /// <summary>
    /// Adds a byte array to the ISO image as a file.
    /// </summary>
    /// <param name="name">The name of the file on the ISO image.</param>
    /// <param name="content">The contents of the file.</param>
    /// <returns>The object representing this file.</returns>
    /// <remarks>
    /// The name is the full path to the file, for example:
    /// <example><code>
    ///   builder.AddFile(@"DIRA\DIRB\FILE.TXT;1", new byte[]{0,1,2});
    /// </code></example>
    /// <para>Note the version number at the end of the file name is optional, if not
    /// specified the default of 1 will be used.</para>
    /// </remarks>
    public BuildFileInfo AddFile(string name, byte[] content)
    {
        CheckDirectoryForFilePath(name, out var nameElements, out var dir);

        var fi = new BuildFileInfo(nameElements[nameElements.Length - 1].ToString(), dir, content);
        AddFile(fi);
        dir.Add(fi);
        return fi;
    }

    /// <summary>
    /// Adds a disk file to the ISO image as a file.
    /// </summary>
    /// <param name="name">The name of the file on the ISO image.</param>
    /// <param name="sourcePath">The name of the file on disk.</param>
    /// <returns>The object representing this file.</returns>
    /// <remarks>
    /// The name is the full path to the file, for example:
    /// <example><code>
    ///   builder.AddFile(@"DIRA\DIRB\FILE.TXT;1", @"C:\temp\tempfile.bin");
    /// </code></example>
    /// <para>Note the version number at the end of the file name is optional, if not
    /// specified the default of 1 will be used.</para>
    /// </remarks>
    public BuildFileInfo AddFile(string name, string sourcePath)
    {
        CheckDirectoryForFilePath(name, out var nameElements, out var dir);

        var fi = new BuildFileInfo(nameElements[nameElements.Length - 1].ToString(), dir, sourcePath);
        AddFile(fi);
        dir.Add(fi);
        return fi;
    }

    /// <summary>
    /// Adds a stream to the ISO image as a file.
    /// </summary>
    /// <param name="name">The name of the file on the ISO image.</param>
    /// <param name="source">The contents of the file.</param>
    /// <returns>The object representing this file.</returns>
    /// <remarks>
    /// The name is the full path to the file, for example:
    /// <example><code>
    ///   builder.AddFile(@"DIRA\DIRB\FILE.TXT;1", stream);
    /// </code></example>
    /// <para>Note the version number at the end of the file name is optional, if not
    /// specified the default of 1 will be used.</para>
    /// </remarks>
    public BuildFileInfo AddFile(string name, Stream source)
    {
        if (!source.CanSeek)
        {
            throw new ArgumentException("source doesn't support seeking", nameof(source));
        }

        CheckDirectoryForFilePath(name, out var nameElements, out var dir);

        var fi = new BuildFileInfo(nameElements[nameElements.Length - 1].ToString(), dir, source);
        AddFile(fi);
        dir.Add(fi);
        return fi;
    }

    protected override List<BuilderExtent> FixExtents(out long totalLength)
    {
        var fixedRegions = new List<BuilderExtent>();

        var buildTime = DateTime.UtcNow;

        var suppEncoding = _buildParams.UseJoliet ? Encoding.BigEndianUnicode : Encoding.ASCII;

        var primaryLocationTable = new Dictionary<BuildDirectoryMember, uint>();
        var supplementaryLocationTable =
            new Dictionary<BuildDirectoryMember, uint>();

        var focus = DiskStart + 3 * IsoUtilities.SectorSize; // Primary, Supplementary, End (fixed at end...)
        if (_bootEntry != null)
        {
            focus += IsoUtilities.SectorSize;
        }

        // ####################################################################
        // # 0. Fix boot image location
        // ####################################################################
        long bootCatalogPos = 0;
        if (_bootEntry != null)
        {
            // Boot catalog MUST be at beginning
            bootCatalogPos = focus;
            focus += IsoUtilities.SectorSize;
            var bootImagePos = focus;
            var realBootImage = PatchBootImage(_bootImage, (uint)(DiskStart / IsoUtilities.SectorSize),
                (uint)(bootImagePos / IsoUtilities.SectorSize));
            var bootImageExtent = new BuilderStreamExtent(focus, realBootImage);
            fixedRegions.Add(bootImageExtent);
            focus += MathUtilities.RoundUp(bootImageExtent.Length, IsoUtilities.SectorSize);

            var bootCatalog = new byte[IsoUtilities.SectorSize];
            var bve = new BootValidationEntry
            {
                ManfId = ManufacturerId
            };
            bve.WriteTo(bootCatalog, 0x00);
            _bootEntry.ImageStart = (uint)MathUtilities.Ceil(bootImagePos, IsoUtilities.SectorSize);
            if (_bootEntry.BootMediaType == BootDeviceEmulation.NoEmulation)
            {
                _bootEntry.SectorCount = (ushort)MathUtilities.Ceil(_bootImage.Length, Sizes.Sector);
            }
            else
            {
                _bootEntry.SectorCount = 1;
            }

            _bootEntry.WriteTo(bootCatalog, 0x20);
            fixedRegions.Add(new BuilderBufferExtent(bootCatalogPos, bootCatalog));
            
            // Don't add to focus, we already skipped the length of the bootCatalog
        }

        // ####################################################################
        // # 1. Fix file locations
        // ####################################################################

        // Find end of the file data, fixing the files in place as we go
        foreach (var fi in _files)
        {
            if (TrackEqualSourceFiles && primaryLocationTable.TryGetValue(fi, out var existing_sector))
            {
                primaryLocationTable.Add(fi, existing_sector);
                supplementaryLocationTable.Add(fi, existing_sector);

                var existing_focus = existing_sector * IsoUtilities.SectorSize;

                var extent = new FileExtent(fi, existing_focus);

                // Only remember files of non-zero length (otherwise we'll stomp on a valid file)
                if (extent.Length != 0)
                {
                    fixedRegions.Add(extent);
                }
            }
            else
            {
                var sector = (uint)(focus / IsoUtilities.SectorSize);

                primaryLocationTable.Add(fi, sector);
                supplementaryLocationTable.Add(fi, sector);
                var extent = new FileExtent(fi, focus);

                // Only remember files of non-zero length (otherwise we'll stomp on a valid file)
                if (extent.Length != 0)
                {
                    fixedRegions.Add(extent);
                }

                focus += MathUtilities.RoundUp(extent.Length, IsoUtilities.SectorSize);
            }
        }

        // ####################################################################
        // # 2. Fix directory locations
        // ####################################################################

        // There are two directory tables
        //  1. Primary        (std ISO9660)
        //  2. Supplementary  (Joliet)

        // Find start of the second set of directory data, fixing ASCII directories in place.
        var startOfFirstDirData = focus;
        foreach (var di in _dirs)
        {
            primaryLocationTable.Add(di, (uint)(focus / IsoUtilities.SectorSize));
            var extent = new DirectoryExtent(di, primaryLocationTable, Encoding.ASCII, focus);
            fixedRegions.Add(extent);
            focus += MathUtilities.RoundUp(extent.Length, IsoUtilities.SectorSize);
        }

        // Find end of the second directory table, fixing supplementary directories in place.
        var startOfSecondDirData = focus;
        foreach (var di in _dirs)
        {
            supplementaryLocationTable.Add(di, (uint)(focus / IsoUtilities.SectorSize));
            var extent = new DirectoryExtent(di, supplementaryLocationTable, suppEncoding, focus);
            fixedRegions.Add(extent);
            focus += MathUtilities.RoundUp(extent.Length, IsoUtilities.SectorSize);
        }

        // ####################################################################
        // # 3. Fix path tables
        // ####################################################################

        // There are four path tables:
        //  1. LE, ASCII
        //  2. BE, ASCII
        //  3. LE, Supp Encoding (Joliet)
        //  4. BE, Supp Encoding (Joliet)

        // Find end of the path table
        var startOfFirstPathTable = focus;
        var pathTable = new PathTable(false, Encoding.ASCII, _dirs, primaryLocationTable, focus);
        fixedRegions.Add(pathTable);
        focus += MathUtilities.RoundUp(pathTable.Length, IsoUtilities.SectorSize);
        var primaryPathTableLength = pathTable.Length;

        var startOfSecondPathTable = focus;
        pathTable = new PathTable(true, Encoding.ASCII, _dirs, primaryLocationTable, focus);
        fixedRegions.Add(pathTable);
        focus += MathUtilities.RoundUp(pathTable.Length, IsoUtilities.SectorSize);

        var startOfThirdPathTable = focus;
        pathTable = new PathTable(false, suppEncoding, _dirs, supplementaryLocationTable, focus);
        fixedRegions.Add(pathTable);
        focus += MathUtilities.RoundUp(pathTable.Length, IsoUtilities.SectorSize);
        var supplementaryPathTableLength = pathTable.Length;

        var startOfFourthPathTable = focus;
        pathTable = new PathTable(true, suppEncoding, _dirs, supplementaryLocationTable, focus);
        fixedRegions.Add(pathTable);
        focus += MathUtilities.RoundUp(pathTable.Length, IsoUtilities.SectorSize);

        // Find the end of the disk
        totalLength = focus;

        // ####################################################################
        // # 4. Prepare volume descriptors now other structures are fixed
        // ####################################################################
        var regionIdx = 0;
        focus = DiskStart;
        var pvDesc = new PrimaryVolumeDescriptor(
            (uint)(totalLength / IsoUtilities.SectorSize), // VolumeSpaceSize
            (uint)primaryPathTableLength, // PathTableSize
            (uint)(startOfFirstPathTable / IsoUtilities.SectorSize), // TypeLPathTableLocation
            (uint)(startOfSecondPathTable / IsoUtilities.SectorSize), // TypeMPathTableLocation
            (uint)(startOfFirstDirData / IsoUtilities.SectorSize), // RootDirectory.LocationOfExtent
            (uint)_rootDirectory.GetDataSize(Encoding.ASCII), // RootDirectory.DataLength
            buildTime)
        {
            VolumeIdentifier = _buildParams.VolumeIdentifier
        };
        var pvdr = new PrimaryVolumeDescriptorRegion(pvDesc, focus);
        fixedRegions.Insert(regionIdx++, pvdr);
        focus += IsoUtilities.SectorSize;

        if (_bootEntry != null)
        {
            var bvDesc = new BootVolumeDescriptor(
                (uint)(bootCatalogPos / IsoUtilities.SectorSize));
            var bvdr = new BootVolumeDescriptorRegion(bvDesc, focus);
            fixedRegions.Insert(regionIdx++, bvdr);
            focus += IsoUtilities.SectorSize;
        }

        var svDesc = new SupplementaryVolumeDescriptor(
            (uint)(totalLength / IsoUtilities.SectorSize), // VolumeSpaceSize
            (uint)supplementaryPathTableLength, // PathTableSize
            (uint)(startOfThirdPathTable / IsoUtilities.SectorSize), // TypeLPathTableLocation
            (uint)(startOfFourthPathTable / IsoUtilities.SectorSize), // TypeMPathTableLocation
            (uint)(startOfSecondDirData / IsoUtilities.SectorSize), // RootDirectory.LocationOfExtent
            (uint)_rootDirectory.GetDataSize(suppEncoding), // RootDirectory.DataLength
            buildTime,
            suppEncoding)
        {
            VolumeIdentifier = _buildParams.VolumeIdentifier
        };
        var svdr = new SupplementaryVolumeDescriptorRegion(svDesc, focus);
        fixedRegions.Insert(regionIdx++, svdr);
        focus += IsoUtilities.SectorSize;

        var evDesc = new VolumeDescriptorSetTerminator();
        var evdr = new VolumeDescriptorSetTerminatorRegion(evDesc, focus);
        fixedRegions.Insert(regionIdx++, evdr);

        return fixedRegions;
    }

    /// <summary>
    /// Patches a boot image (esp. for ISOLINUX) before it is written to the disk.
    /// </summary>
    /// <param name="bootImage">The original (master) boot image.</param>
    /// <param name="pvdLba">The logical block address of the primary volume descriptor.</param>
    /// <param name="bootImageLba">The logical block address of the boot image itself.</param>
    /// <returns>A stream containing the patched boot image - does not need to be disposed.</returns>
    private Stream PatchBootImage(Stream bootImage, uint pvdLba, uint bootImageLba)
    {
        // Early-exit if no patching to do...
        if (!UpdateIsolinuxBootTable)
        {
            return bootImage;
        }

        var bootData = bootImage.ReadExactly((int)bootImage.Length);

        Array.Clear(bootData, 8, 56);

        uint checkSum = 0;
        for (var i = 64; i < bootData.Length; i += 4)
        {
            checkSum += EndianUtilities.ToUInt32LittleEndian(bootData, i);
        }

        EndianUtilities.WriteBytesLittleEndian(pvdLba, bootData, 8);
        EndianUtilities.WriteBytesLittleEndian(bootImageLba, bootData, 12);
        EndianUtilities.WriteBytesLittleEndian(bootData.Length, bootData, 16);
        EndianUtilities.WriteBytesLittleEndian(checkSum, bootData, 20);

        return new MemoryStream(bootData, false);
    }

    /// <summary>
    /// Returns the number of files that have been added to this instance.
    /// </summary>
    public int FileCount => _files.Count;

    int IFileSystemBuilder.FileCount => throw new NotImplementedException();

    long IFileSystemBuilder.TotalSize => throw new NotImplementedException();

    string IFileSystemBuilder.VolumeIdentifier { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    /// <summary>
    /// Get information about already added file.
    /// </summary>
    /// <param name="path">Full path to previously added file.</param>
    /// <returns>BuildDirectoryMember object representing already added file.</returns>
    public BuildDirectoryMember GetFile(string path)
    {
        var nameElements = path.AsMemory().Split('\\', '/', StringSplitOptions.RemoveEmptyEntries).ToArray();
        var dir = GetDirectory(nameElements, nameElements.Length - 1, true);

        var name = nameElements[nameElements.Length - 1].ToString();

        if (dir.TryGetMember(name, out var existing))
        {
            return existing;
        }

        name = IsoUtilities.NormalizeFileName(name);

        if (dir.TryGetMember(name, out existing))
        {
            return existing;
        }

        return null;
    }

    private void CheckDirectoryForFilePath(string name, out ReadOnlyMemory<char>[] nameElements, out BuildDirectoryInfo dir)
    {
        nameElements = name.AsMemory().Split('\\', '/', StringSplitOptions.RemoveEmptyEntries).ToArray();
        dir = GetDirectory(nameElements, nameElements.Length - 1, true);

        if (dir.TryGetMember(nameElements[nameElements.Length - 1].ToString(), out _))
        {
            throw new IOException("File already exists");
        }
    }

    private BuildDirectoryInfo GetDirectory(ReadOnlyMemory<char>[] path, int pathLength, bool createMissing)
    {
        var di = TryGetDirectory(path, pathLength, createMissing)
            ?? throw new DirectoryNotFoundException("Directory not found");

        return di;
    }

    private BuildDirectoryInfo TryGetDirectory(ReadOnlyMemory<char>[] path, int pathLength, bool createMissing)
    {
        var focus = _rootDirectory;

        for (var i = 0; i < pathLength; ++i)
        {
            if (!focus.TryGetMember(path[i].ToString(), out var next))
            {
                if (createMissing)
                {
                    // This directory doesn't exist, create it...
                    var di = new BuildDirectoryInfo(path[i].ToString(), focus);
                    focus.Add(di);
                    _dirs.Add(di);
                    focus = di;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                if (next is not BuildDirectoryInfo nextAsBuildDirectoryInfo)
                {
                    throw new IOException("File with conflicting name exists");
                }

                focus = nextAsBuildDirectoryInfo;
            }
        }

        return focus;
    }

    void IFileSystemBuilder.AddDirectory(string name) =>
        AddDirectory(name);

    void IFileSystemBuilder.AddFile(string name, byte[] content) =>
        AddFile(name, content);

    void IFileSystemBuilder.AddFile(string name, Stream source) =>
        AddFile(name, source);

    void IFileSystemBuilder.AddFile(string name, string sourcePath) =>
        AddFile(name, sourcePath);

    void IFileSystemBuilder.AddDirectory(string name, int ownerId, int groupId, UnixFilePermissions fileMode, DateTime modificationTime) =>
        AddDirectory(name).CreationTime = modificationTime;

    void IFileSystemBuilder.AddFile(string name, byte[] content, int ownerId, int groupId, UnixFilePermissions fileMode, DateTime modificationTime) =>
        AddFile(name, content).CreationTime = modificationTime;

    void IFileSystemBuilder.AddFile(string name, Stream source, int ownerId, int groupId, UnixFilePermissions fileMode, DateTime modificationTime) =>
        AddFile(name, source).CreationTime = modificationTime;

    void IFileSystemBuilder.AddFile(string name, string sourcePath, int ownerId, int groupId, UnixFilePermissions fileMode, DateTime modificationTime) =>
        AddFile(name, sourcePath).CreationTime = modificationTime;

    void IFileSystemBuilder.AddDirectory(string name, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes) =>
        AddDirectory(name).CreationTime = writtenTime;

    void IFileSystemBuilder.AddFile(string name, byte[] content, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes) =>
        AddFile(name, content).CreationTime = writtenTime;

    void IFileSystemBuilder.AddFile(string name, Stream source, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes) =>
        AddFile(name, source).CreationTime = writtenTime;
    
    void IFileSystemBuilder.AddFile(string name, string sourcePath, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes) =>
        AddFile(name, sourcePath).CreationTime = writtenTime;

    bool IFileSystemBuilder.Exists(string path) => GetFile(path) != null;

    public IFileSystem GenerateFileSystem() => new CDReader(Build(), UseJoliet);
}