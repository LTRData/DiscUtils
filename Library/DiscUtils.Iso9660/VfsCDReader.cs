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
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiscUtils.Partitions;
using DiscUtils.Streams;
using DiscUtils.Vfs;
using LTRData.Extensions.Buffers;

namespace DiscUtils.Iso9660;

internal class VfsCDReader : VfsReadOnlyFileSystem<ReaderDirEntry, File, ReaderDirectory, IsoContext>,
                             IClusterBasedFileSystem, IUnixFileSystem, IFileSystemWithClusterMap
{
    public override bool IsCaseSensitive => false;

    private static readonly Iso9660Variant[] DefaultVariantsNoJoliet = { Iso9660Variant.RockRidge, Iso9660Variant.Iso9660 };

    private static readonly Iso9660Variant[] DefaultVariantsWithJoliet = { Iso9660Variant.Joliet, Iso9660Variant.RockRidge, Iso9660Variant.Iso9660 };

    private byte[] _bootCatalog;
    private readonly BootVolumeDescriptor _bootVolDesc;

    private readonly Stream _data;
    private readonly bool _hideVersions;

    /// <summary>
    /// Initializes a new instance of the VfsCDReader class.
    /// </summary>
    /// <param name="data">The stream to read the ISO image from.</param>
    /// <param name="joliet">Whether to read Joliet extensions.</param>
    /// <param name="hideVersions">Hides version numbers (e.g. ";1") from the end of files.</param>
    public VfsCDReader(Stream data, bool joliet, bool hideVersions)
        : this(data, joliet ? DefaultVariantsWithJoliet : DefaultVariantsNoJoliet, hideVersions) {}

    /// <summary>
    /// Initializes a new instance of the VfsCDReader class.
    /// </summary>
    /// <param name="data">The stream to read the ISO image from.</param>
    /// <param name="variantPriorities">Which possible file system variants to use, and with which priority.</param>
    /// <param name="hideVersions">Hides version numbers (e.g. ";1") from the end of files.</param>
    /// <remarks>
    /// <para>
    /// The implementation considers each of the file system variants in <c>variantProperties</c> and selects
    /// the first which is determined to be present.  In this example Joliet, then Rock Ridge, then vanilla
    /// Iso9660 will be considered:
    /// </para>
    /// <code lang="cs">
    /// VfsCDReader(stream, new Iso9660Variant[] {Joliet, RockRidge, Iso9660}, true);
    /// </code>
    /// <para>The Iso9660 variant should normally be specified as the final entry in the list.  Placing it earlier
    /// in the list will effectively mask later items and not including it may prevent some ISOs from being read.</para>
    /// </remarks>
    public VfsCDReader(Stream data, Iso9660Variant[] variantPriorities, bool hideVersions)
        : base(new DiscFileSystemOptions())
    {
        _data = data;
        _hideVersions = hideVersions;

        long vdpos = 0x8000; // Skip lead-in

        var buffer = new byte[IsoUtilities.SectorSize];

        long pvdPos = 0;
        long svdPos = 0;

        BaseVolumeDescriptor bvd;
        do
        {
            data.Position = vdpos;
            var numRead = data.Read(buffer, 0, IsoUtilities.SectorSize);
            if (numRead != IsoUtilities.SectorSize)
            {
                break;
            }

            bvd = new BaseVolumeDescriptor(buffer);

            if (bvd.StandardIdentifier != BaseVolumeDescriptor.Iso9660StandardIdentifier)
            {
                throw new InvalidFileSystemException("Volume is not ISO-9660");
            }

            switch (bvd.VolumeDescriptorType)
            {
                case VolumeDescriptorType.Boot:
                    _bootVolDesc = new BootVolumeDescriptor(buffer);
                    if (_bootVolDesc.SystemId != BootVolumeDescriptor.ElToritoSystemIdentifier)
                    {
                        _bootVolDesc = null;
                    }

                    break;

                case VolumeDescriptorType.Primary: // Primary Vol Descriptor
                    pvdPos = vdpos;
                    break;

                case VolumeDescriptorType.Supplementary: // Supplementary Vol Descriptor
                    svdPos = vdpos;
                    break;

                case VolumeDescriptorType.Partition: // Volume Partition Descriptor
                    break;
                case VolumeDescriptorType.SetTerminator: // Volume Descriptor Set Terminator
                    break;
            }

            vdpos += IsoUtilities.SectorSize;
        } while (bvd.VolumeDescriptorType != VolumeDescriptorType.SetTerminator);

        ActiveVariant = Iso9660Variant.None;
        foreach (var variant in variantPriorities)
        {
            switch (variant)
            {
                case Iso9660Variant.Joliet:
                    if (svdPos != 0)
                    {
                        data.Position = svdPos;
                        data.ReadExactly(buffer, 0, IsoUtilities.SectorSize);
                        var volDesc = new SupplementaryVolumeDescriptor(buffer);

                        Context = new IsoContext { VolumeDescriptor = volDesc, DataStream = _data };
                        RootDirectory = new ReaderDirectory(Context,
                            new ReaderDirEntry(Context, volDesc.RootDirectory));
                        ActiveVariant = Iso9660Variant.Iso9660;
                    }

                    break;

                case Iso9660Variant.RockRidge:
                case Iso9660Variant.Iso9660:
                    if (pvdPos != 0)
                    {
                        data.Position = pvdPos;
                        data.ReadExactly(buffer, 0, IsoUtilities.SectorSize);
                        var volDesc = new PrimaryVolumeDescriptor(buffer);

                        var context = new IsoContext { VolumeDescriptor = volDesc, DataStream = _data };
                        var rootSelfRecord = ReadRootSelfRecord(context);

                        InitializeSusp(context, rootSelfRecord);

                        if (variant == Iso9660Variant.Iso9660
                            ||
                            (variant == Iso9660Variant.RockRidge &&
                             !string.IsNullOrEmpty(context.RockRidgeIdentifier)))
                        {
                            Context = context;
                            RootDirectory = new ReaderDirectory(context, new ReaderDirEntry(context, rootSelfRecord));
                            ActiveVariant = variant;
                        }
                    }

                    break;
            }

            if (ActiveVariant != Iso9660Variant.None)
            {
                break;
            }
        }

        if (ActiveVariant == Iso9660Variant.None)
        {
            throw new IOException("None of the permitted ISO9660 file system variants was detected");
        }
    }

    public Iso9660Variant ActiveVariant { get; }

    public BootDeviceEmulation BootEmulation
    {
        get
        {
            var initialEntry = GetBootInitialEntry();
            if (initialEntry != null)
            {
                return initialEntry.BootMediaType;
            }

            return BootDeviceEmulation.NoEmulation;
        }
    }

    public long BootImageStart
    {
        get
        {
            var initialEntry = GetBootInitialEntry();
            if (initialEntry != null)
            {
                return initialEntry.ImageStart * IsoUtilities.SectorSize;
            }

            return 0;
        }
    }

    public int BootLoadSegment
    {
        get
        {
            var initialEntry = GetBootInitialEntry();
            if (initialEntry != null)
            {
                return initialEntry.LoadSegment;
            }

            return 0;
        }
    }

    /// <summary>
    /// Provides the friendly name for the CD filesystem.
    /// </summary>
    public override string FriendlyName => "ISO 9660 (CD-ROM)";

    public bool HasBootImage
    {
        get
        {
            if (_bootVolDesc == null)
            {
                return false;
            }

            var bootCatalog = GetBootCatalog();
            if (bootCatalog == null)
            {
                return false;
            }

            var entry = new BootValidationEntry(bootCatalog, 0);
            return entry.ChecksumValid;
        }
    }

    /// <summary>
    /// Gets the Volume Identifier.
    /// </summary>
    public override string VolumeLabel => Context.VolumeDescriptor.VolumeIdentifier;

    public int SectorSize => IsoUtilities.SectorSize;

    public long TotalSectors => Context.VolumeDescriptor.VolumeSpaceSize;

    public long ClusterSize => SectorSize;

    public long TotalClusters => TotalSectors;

    public long ClusterToOffset(long cluster)
    {
        return cluster * ClusterSize;
    }

    public long OffsetToCluster(long offset)
    {
        return offset / ClusterSize;
    }

    /// <summary>
    /// Size of the Filesystem in bytes
    /// </summary>
    public override long Size => throw new NotSupportedException("Filesystem size is not (yet) supported");

    /// <summary>
    /// Used space of the Filesystem in bytes
    /// </summary>
    public override long UsedSpace => throw new NotSupportedException("Filesystem size is not (yet) supported");

    /// <summary>
    /// Available space of the Filesystem in bytes
    /// </summary>
    public override long AvailableSpace => throw new NotSupportedException("Filesystem size is not (yet) supported");

    public override bool SupportsUsedAvailableSpace => false;

    public IEnumerable<Range<long, long>> PathToClusters(string path)
    {
        var entry = GetDirectoryEntry(path)
            ?? throw new FileNotFoundException("File not found", path);

        if (entry.Record.FileUnitSize != 0 || entry.Record.InterleaveGapSize != 0)
        {
            throw new NotSupportedException("Non-contiguous extents not supported");
        }

        return SingleValueEnumerable.Get(
            new Range<long, long>(entry.Record.LocationOfExtent,
                MathUtilities.Ceil(entry.Record.DataLength, IsoUtilities.SectorSize)));
    }

    public IEnumerable<StreamExtent> PathToExtents(string path)
    {
        var entry = GetDirectoryEntry(path)
            ?? throw new FileNotFoundException("File not found", path);

        if (entry.Record.FileUnitSize != 0 || entry.Record.InterleaveGapSize != 0)
        {
            throw new NotSupportedException("Non-contiguous extents not supported");
        }

        return SingleValueEnumerable.Get(
            new StreamExtent(entry.Record.LocationOfExtent * IsoUtilities.SectorSize, entry.Record.DataLength));
    }

    public long GetAllocatedClustersCount(string path)
    {
        var entry = GetDirectoryEntry(path)
            ?? throw new FileNotFoundException("File not found", path);

        if (entry.Record.FileUnitSize != 0 || entry.Record.InterleaveGapSize != 0)
        {
            throw new NotSupportedException("Non-contiguous extents not supported");
        }

        return entry.Record.DataLength / IsoUtilities.SectorSize;
    }

    public ClusterMap BuildClusterMap()
    {
        var totalClusters = TotalClusters;
        var clusterToRole = new ClusterRoles[totalClusters];
        var clusterToFileId = new Dictionary<long, long>();
        var fileIdToPaths = new Dictionary<long, IList<string>>();

        ForAllDirEntries(
            string.Empty,
            (path, entry) =>
            {
                IList<string> paths = null;
                if (fileIdToPaths.TryGetValue(entry.UniqueCacheId, out var value))
                {
                    paths = value;
                }

                if (paths == null)
                {
                    fileIdToPaths[entry.UniqueCacheId] = Array.AsReadOnly(new[] { path });
                }
                else
                {
                    var newPaths = new string[paths.Count + 1];
                    paths.CopyTo(newPaths, 0);
                    newPaths[paths.Count] = path;
                    fileIdToPaths[entry.UniqueCacheId] = Array.AsReadOnly(newPaths);
                }

                if (entry.Record.FileUnitSize != 0 || entry.Record.InterleaveGapSize != 0)
                {
                    throw new NotSupportedException("Non-contiguous extents not supported");
                }

                long clusters = MathUtilities.Ceil(entry.Record.DataLength, IsoUtilities.SectorSize);
                for (long i = 0; i < clusters; ++i)
                {
                    clusterToRole[i + entry.Record.LocationOfExtent] = ClusterRoles.DataFile;
                    clusterToFileId[i + entry.Record.LocationOfExtent] = entry.UniqueCacheId;
                }
            });

        return new ClusterMap(clusterToRole, clusterToFileId, fileIdToPaths);
    }

    public UnixFileSystemInfo GetUnixFileInfo(string path)
    {
        var file = GetFile(path);
        return file.UnixFileInfo;
    }

    /// <summary>
    /// Detects size of hard disk emulation image from partition table.
    /// </summary>
    /// <param name="stream">The stream to inspect.</param>
    /// <returns><c>Sector count</c> if the stream appears to be a FAT file system, else <c>0</c>.</returns>
    public static long DetectSectorCountFromPartitionTable(Stream stream)
    {
        if (stream.Length < 512)
        {
            return 0;
        }

        stream.Position = 0;

        Span<byte> bytes = stackalloc byte[512];
        stream.ReadExactly(bytes);

        var disk = new BiosPartitionTable(stream, Geometry.Null);

        try
        {
            var partition = disk.Partitions.Single();

            var imageSize = partition.LastSector + 1;

            return imageSize;
        }
        catch
        {
            throw new InvalidOperationException("Invalid hard disk emulation boot image - needs exactly one partition");
        }
    }

    /// <summary>
    /// Detects size of FAT file system from a disk's boot sector.
    /// </summary>
    /// <param name="stream">The stream to inspect.</param>
    /// <returns><c>Sector count</c> if the stream appears to be a FAT file system, else <c>0</c>.</returns>
    public static ushort DetectSectorCountFromBootSector(Stream stream)
    {
        if (stream.Length < 512)
        {
            return 0;
        }

        stream.Position = 0;

        Span<byte> bytes = stackalloc byte[512];
        stream.ReadExactly(bytes);
        
        var bpbBytesPerSec = EndianUtilities.ToUInt16LittleEndian(bytes.Slice(11));
        
        if (bpbBytesPerSec != 512)
        {
            return 0;
        }

        var bpbNumFATs = bytes[16];
        if (bpbNumFATs is 0 or > 2)
        {
            return 0;
        }

        var bpbTotSec16 = EndianUtilities.ToUInt16LittleEndian(bytes.Slice(19));
        var bpbTotSec32 = EndianUtilities.ToUInt32LittleEndian(bytes.Slice(32));

        if (!((bpbTotSec16 == 0) ^ (bpbTotSec32 == 0)))
        {
            return 0;
        }

        var totalSectors = bpbTotSec16 + bpbTotSec32;
        // Can't be greater than unsigned short
        if (totalSectors > 65535)
        {
            return 0;
        }

        return (ushort)totalSectors;
    }


    /// <summary>
    /// Returns the actual sector count for a boot image (vs. reported).
    /// 
    /// Boot Images with MBR or floppy boot sectors will have boot Entries that 
    /// list their sector count as 1.
    /// This is incorrect and is either determined by the emulation type or what
    /// the boot sector lists the partition as having.
    /// </summary>
    /// <returns>int: sector count in 512 bytes</returns>
    public long GetActualBootImageSectorCount()
    {
        switch (BootEmulation)
        {
            case BootDeviceEmulation.Diskette1440KiB:
                return 1440 * 1024 / Sizes.Sector;
            case BootDeviceEmulation.Diskette1200KiB:
                return 1200 * 1024 / Sizes.Sector;
            case BootDeviceEmulation.Diskette2880KiB:
                return 2880 * 1024 / Sizes.Sector;
            case BootDeviceEmulation.HardDisk:
                {
                    var initialEntry = GetBootInitialEntry();
                    var BytesToStart = initialEntry.ImageStart * IsoUtilities.SectorSize;
                    var sectorCount = DetectSectorCountFromPartitionTable(
                        new SubStream(_data, BytesToStart, Sizes.Sector)
                        );
                    // Invalid length read from BootSector
                    if (sectorCount == 0 || sectorCount * Sizes.Sector + BytesToStart > _data.Length)
                    {
                        sectorCount = initialEntry.SectorCount;
                    }

                    return sectorCount;
                }
            case BootDeviceEmulation.NoEmulation:
            default:
                {
                    var initialEntry = GetBootInitialEntry();
                    var BytesToStart = initialEntry.ImageStart * IsoUtilities.SectorSize;
                    var sectorCount = DetectSectorCountFromBootSector(
                        new SubStream(_data, BytesToStart, Sizes.Sector)
                        );
                    // Invalid length read from BootSector
                    if (sectorCount == 0 || sectorCount * Sizes.Sector + BytesToStart > _data.Length)
                    {
                        sectorCount = initialEntry.SectorCount;
                    }

                    return sectorCount;
                }
        }
    }

    public Stream OpenBootImage()
    {
        var initialEntry = GetBootInitialEntry();
        var sectorCount = GetActualBootImageSectorCount();
        if (initialEntry != null)
        {
            return new SubStream(_data, initialEntry.ImageStart * IsoUtilities.SectorSize,
                sectorCount * Sizes.Sector);
        }

        throw new InvalidOperationException("No valid boot image");
    }

    protected override File ConvertDirEntryToFile(ReaderDirEntry dirEntry)
    {
        if (dirEntry.IsDirectory)
        {
            return new ReaderDirectory(Context, dirEntry);
        }

        return new File(Context, dirEntry);
    }

    protected override string FormatFileName(string name)
    {
        if (_hideVersions)
        {
            var pos = name.LastIndexOf(';');
            if (pos > 0)
            {
                return name.Substring(0, pos);
            }
        }

        return name;
    }

    private static void InitializeSusp(IsoContext context, DirectoryRecord rootSelfRecord)
    {
        // Stage 1 - SUSP present?
        var extensions = new List<SuspExtension>();
        if (!SuspRecords.DetectSharingProtocol(rootSelfRecord.SystemUseData))
        {
            context.SuspExtensions = [];
            context.SuspDetected = false;
            return;
        }

        context.SuspDetected = true;

        var suspRecords = new SuspRecords(context, rootSelfRecord.SystemUseData);

        // Stage 2 - Init general SUSP params
        var spEntry =
            (SharingProtocolSystemUseEntry)suspRecords.GetEntries(null, "SP")[0];
        context.SuspSkipBytes = spEntry.SystemAreaSkip;

        // Stage 3 - Init extensions
        var extensionEntries = suspRecords.GetEntries(null, "ER");
        if (extensionEntries != null)
        {
            foreach (ExtensionSystemUseEntry extension in extensionEntries)
            {
                switch (extension.ExtensionIdentifier)
                {
                    case "RRIP_1991A":
                    case "IEEE_P1282":
                    case "IEEE_1282":
                        extensions.Add(new RockRidgeExtension(extension.ExtensionIdentifier));
                        context.RockRidgeIdentifier = extension.ExtensionIdentifier;
                        break;

                    default:
                        extensions.Add(new GenericSuspExtension(extension.ExtensionIdentifier));
                        break;
                }
            }
        }
        else if (suspRecords.GetEntries(null, "RR") != null)
        {
            // Some ISO creators don't add the 'ER' record for RockRidge, but write the (legacy)
            // RR record anyway
            extensions.Add(new RockRidgeExtension("RRIP_1991A"));
            context.RockRidgeIdentifier = "RRIP_1991A";
        }

        context.SuspExtensions = extensions;
    }

    private static DirectoryRecord ReadRootSelfRecord(IsoContext context)
    {
        context.DataStream.Position = context.VolumeDescriptor.RootDirectory.LocationOfExtent *
                                      context.VolumeDescriptor.LogicalBlockSize;
        
        var firstSector = ArrayPool<byte>.Shared.Rent(context.VolumeDescriptor.LogicalBlockSize);
        try
        {
            context.DataStream.ReadExactly(firstSector, 0, context.VolumeDescriptor.LogicalBlockSize);

            DirectoryRecord.ReadFrom(firstSector, context.VolumeDescriptor.CharacterEncoding, out var rootSelfRecord);
            return rootSelfRecord;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(firstSector);
        }
    }

    private BootInitialEntry GetBootInitialEntry()
    {
        var bootCatalog = GetBootCatalog();
        if (bootCatalog == null)
        {
            return null;
        }

        var validationEntry = new BootValidationEntry(bootCatalog, 0);
        if (!validationEntry.ChecksumValid)
        {
            return null;
        }

        return new BootInitialEntry(bootCatalog, 0x20);
    }

    private byte[] GetBootCatalog()
    {
        if (_bootCatalog == null && _bootVolDesc != null)
        {
            _data.Position = _bootVolDesc.CatalogSector * IsoUtilities.SectorSize;
            _bootCatalog = _data.ReadExactly(IsoUtilities.SectorSize);
        }

        return _bootCatalog;
    }
}
