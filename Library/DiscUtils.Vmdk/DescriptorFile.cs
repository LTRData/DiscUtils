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
using System.Globalization;
using System.IO;
using System.Text;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Vmdk;

internal class DescriptorFile
{
    private const string HeaderVersion = "version";
    private const string HeaderContentId = "CID";
    private const string HeaderParentContentId = "parentCID";
    private const string HeaderCreateType = "createType";
    private const string HeaderParentFileNameHint = "parentFileNameHint";

    private const string DiskDbAdapterType = "ddb.adapterType";
    private const string DiskDbSectors = "ddb.geometry.sectors";
    private const string DiskDbHeads = "ddb.geometry.heads";
    private const string DiskDbCylinders = "ddb.geometry.cylinders";
    private const string DiskDbBiosSectors = "ddb.geometry.biosSectors";
    private const string DiskDbBiosHeads = "ddb.geometry.biosHeads";
    private const string DiskDbBiosCylinders = "ddb.geometry.biosCylinders";
    private const string DiskDbHardwareVersion = "ddb.virtualHWVersion";
    private const string DiskDbUuid = "ddb.uuid";

    private const long MaxSize = 20 * Sizes.OneKiB;
    private readonly List<DescriptorFileEntry> _diskDataBase;

    private readonly List<DescriptorFileEntry> _header;

    public DescriptorFile()
    {
        _header = [];
        Extents = [];
        _diskDataBase = [];

        _header.Add(new DescriptorFileEntry(HeaderVersion, "1", DescriptorFileEntryType.Plain));
        _header.Add(new DescriptorFileEntry(HeaderContentId, "ffffffff", DescriptorFileEntryType.Plain));
        _header.Add(new DescriptorFileEntry(HeaderParentContentId, "ffffffff", DescriptorFileEntryType.Plain));
        _header.Add(new DescriptorFileEntry(HeaderCreateType, string.Empty, DescriptorFileEntryType.Quoted));
    }

    public DescriptorFile(Stream source)
    {
        _header = [];
        Extents = [];
        _diskDataBase = [];

        Load(source);
    }

    public DiskAdapterType AdapterType
    {
        get => ParseAdapterType(GetDiskDatabase(DiskDbAdapterType));
        set => SetDiskDatabase(DiskDbAdapterType, FormatAdapterType(value));
    }

    public Geometry? BiosGeometry
    {
        get
        {
            var cylStr = GetDiskDatabase(DiskDbBiosCylinders);
            var headsStr = GetDiskDatabase(DiskDbBiosHeads);
            var sectorsStr = GetDiskDatabase(DiskDbBiosSectors);
            if (!string.IsNullOrEmpty(cylStr) && !string.IsNullOrEmpty(headsStr) &&
                !string.IsNullOrEmpty(sectorsStr))
            {
                return new Geometry(
                    int.Parse(cylStr, CultureInfo.InvariantCulture),
                    int.Parse(headsStr, CultureInfo.InvariantCulture),
                    int.Parse(sectorsStr, CultureInfo.InvariantCulture));
            }

            return null;
        }
        set
        {
            if (value is null)
            {
                throw new InvalidOperationException("A valid geometry is needed");
            }

            SetDiskDatabase(DiskDbBiosCylinders, value.Value.Cylinders.ToString(CultureInfo.InvariantCulture));
            SetDiskDatabase(DiskDbBiosHeads, value.Value.HeadsPerCylinder.ToString(CultureInfo.InvariantCulture));
            SetDiskDatabase(DiskDbBiosSectors, value.Value.SectorsPerTrack.ToString(CultureInfo.InvariantCulture));
        }
    }

    public uint ContentId
    {
        get => uint.Parse(GetHeader(HeaderContentId), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        set => SetHeader(HeaderContentId, value.ToString("x8", CultureInfo.InvariantCulture),
                DescriptorFileEntryType.Plain);
    }

    public DiskCreateType CreateType
    {
        get => ParseCreateType(GetHeader(HeaderCreateType));
        set => SetHeader(HeaderCreateType, FormatCreateType(value), DescriptorFileEntryType.Plain);
    }

    public Geometry? DiskGeometry
    {
        get
        {
            var cylStr = GetDiskDatabase(DiskDbCylinders);
            var headsStr = GetDiskDatabase(DiskDbHeads);
            var sectorsStr = GetDiskDatabase(DiskDbSectors);
            if (!string.IsNullOrEmpty(cylStr) && !string.IsNullOrEmpty(headsStr) &&
                !string.IsNullOrEmpty(sectorsStr))
            {
                return new Geometry(
                    int.Parse(cylStr, CultureInfo.InvariantCulture),
                    int.Parse(headsStr, CultureInfo.InvariantCulture),
                    int.Parse(sectorsStr, CultureInfo.InvariantCulture));
            }

            return null;
        }

        set
        {
            if (value is null)
            {
                throw new InvalidOperationException("A valid geometry is needed");
            }

            SetDiskDatabase(DiskDbCylinders, value.Value.Cylinders.ToString(CultureInfo.InvariantCulture));
            SetDiskDatabase(DiskDbHeads, value.Value.HeadsPerCylinder.ToString(CultureInfo.InvariantCulture));
            SetDiskDatabase(DiskDbSectors, value.Value.SectorsPerTrack.ToString(CultureInfo.InvariantCulture));
        }
    }

    public List<ExtentDescriptor> Extents { get; }

    public string HardwareVersion
    {
        get => GetDiskDatabase(DiskDbHardwareVersion);
        set => SetDiskDatabase(DiskDbHardwareVersion, value);
    }

    public uint ParentContentId
    {
        get => uint.Parse(GetHeader(HeaderParentContentId), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        set => SetHeader(HeaderParentContentId, value.ToString("x8", CultureInfo.InvariantCulture),
                DescriptorFileEntryType.Plain);
    }

    public string ParentFileNameHint
    {
        get => GetHeader(HeaderParentFileNameHint);
        set => SetHeader(HeaderParentFileNameHint, value, DescriptorFileEntryType.Quoted);
    }

    public Guid UniqueId
    {
        get => ParseUuid(GetDiskDatabase(DiskDbUuid).AsSpan());
        set => SetDiskDatabase(DiskDbUuid, FormatUuid(value));
    }

    internal void Write(Stream stream)
    {
        var content = new StringBuilder();

        content.Append("# Disk DescriptorFile\n");
        for (var i = 0; i < _header.Count; ++i)
        {
            content.Append($"{_header[i].ToString(false)}\n");
        }

        content.Append('\n');
        content.Append("# Extent description\n");
        for (var i = 0; i < Extents.Count; ++i)
        {
            content.Append($"{Extents[i]}\n");
        }

        content.Append('\n');
        content.Append("# The Disk Data Base\n");
        content.Append("#DDB\n");
        for (var i = 0; i < _diskDataBase.Count; ++i)
        {
            content.Append($"{_diskDataBase[i].ToString(true)}\n");
        }

        var contentBytes = Encoding.ASCII.GetBytes(content.ToString());
        stream.Write(contentBytes, 0, contentBytes.Length);
    }

    private static DiskAdapterType ParseAdapterType(string value)
    {
        return value switch
        {
            "ide" => DiskAdapterType.Ide,
            "buslogic" => DiskAdapterType.BusLogicScsi,
            "lsilogic" => DiskAdapterType.LsiLogicScsi,
            "legacyESX" => DiskAdapterType.LegacyEsx,
            _ => throw new ArgumentException(
                                $"Unknown type: {value}", nameof(value)),
        };
    }

    private static string FormatAdapterType(DiskAdapterType value)
    {
        return value switch
        {
            DiskAdapterType.Ide => "ide",
            DiskAdapterType.BusLogicScsi => "buslogic",
            DiskAdapterType.LsiLogicScsi => "lsilogic",
            DiskAdapterType.LegacyEsx => "legacyESX",
            _ => throw new ArgumentException(
                                $"Unknown type: {value}", nameof(value)),
        };
    }

    private static DiskCreateType ParseCreateType(string value)
    {
        return value switch
        {
            "monolithicSparse" => DiskCreateType.MonolithicSparse,
            "vmfsSparse" => DiskCreateType.VmfsSparse,
            "monolithicFlat" => DiskCreateType.MonolithicFlat,
            "vmfs" => DiskCreateType.Vmfs,
            "twoGbMaxExtentSparse" => DiskCreateType.TwoGbMaxExtentSparse,
            "twoGbMaxExtentFlat" => DiskCreateType.TwoGbMaxExtentFlat,
            "fullDevice" => DiskCreateType.FullDevice,
            "vmfsRaw" => DiskCreateType.VmfsRaw,
            "partitionedDevice" => DiskCreateType.PartitionedDevice,
            "vmfsRawDeviceMap" => DiskCreateType.VmfsRawDeviceMap,
            "vmfsPassthroughRawDeviceMap" => DiskCreateType.VmfsPassthroughRawDeviceMap,
            "streamOptimized" => DiskCreateType.StreamOptimized,
            "seSparse" => DiskCreateType.SeSparse,
            "vsanSparse" => DiskCreateType.VsanSparse,
            _ => throw new ArgumentException(
                                $"Unknown type: {value}", nameof(value)),
        };
    }

    private static string FormatCreateType(DiskCreateType value)
    {
        return value switch
        {
            DiskCreateType.MonolithicSparse => "monolithicSparse",
            DiskCreateType.VmfsSparse => "vmfsSparse",
            DiskCreateType.MonolithicFlat => "monolithicFlat",
            DiskCreateType.Vmfs => "vmfs",
            DiskCreateType.TwoGbMaxExtentSparse => "twoGbMaxExtentSparse",
            DiskCreateType.TwoGbMaxExtentFlat => "twoGbMaxExtentFlat",
            DiskCreateType.FullDevice => "fullDevice",
            DiskCreateType.VmfsRaw => "vmfsRaw",
            DiskCreateType.PartitionedDevice => "partitionedDevice",
            DiskCreateType.VmfsRawDeviceMap => "vmfsRawDeviceMap",
            DiskCreateType.VmfsPassthroughRawDeviceMap => "vmfsPassthroughRawDeviceMap",
            DiskCreateType.StreamOptimized => "streamOptimized",
            DiskCreateType.SeSparse => "seSparse",
            DiskCreateType.VsanSparse => "vsanSparse",
            _ => throw new ArgumentException(
                                $"Unknown type: {value}", nameof(value)),
        };
    }

    private static Guid ParseUuid(ReadOnlySpan<char> chars)
    {
        Span<byte> data = stackalloc byte[16];

        for (var i = 0; i < 16; ++i)
        {
            var field = chars;

            var fieldEnd = field.IndexOfAny(' ', '-');
            if (fieldEnd > 0)
            {
                field = chars.Slice(0, fieldEnd);
                chars = chars.Slice(fieldEnd + 1);
            }
            else
            {
                chars = default;
            }

            if (field.IsEmpty)
            {
                throw new ArgumentException("Invalid UUID", nameof(chars));
            }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
            data[i] = byte.Parse(field, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
#else
            data[i] = byte.Parse(field.ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
#endif
        }

        return EndianUtilities.ToGuidLittleEndian(data);
    }

    private static string FormatUuid(Guid value)
    {
        var data = value.ToByteArray();
        return $"{data[0]:x2} {data[1]:x2} {data[2]:x2} {data[3]:x2} {data[4]:x2} {data[5]:x2} {data[6]:x2} {data[7]:x2}-{data[8]:x2} {data[9]:x2} {data[10]:x2} {data[11]:x2} {data[12]:x2} {data[13]:x2} {data[14]:x2} {data[15]:x2}";
    }

    private string GetHeader(string key)
    {
        foreach (var entry in _header)
        {
            if (entry.Key == key)
            {
                return entry.Value;
            }
        }

        return null;
    }

    private void SetHeader(string key, string newValue, DescriptorFileEntryType type)
    {
        foreach (var entry in _header)
        {
            if (entry.Key == key)
            {
                entry.Value = newValue;
                return;
            }
        }

        _header.Add(new DescriptorFileEntry(key, newValue, type));
    }

    private string GetDiskDatabase(string key)
    {
        foreach (var entry in _diskDataBase)
        {
            if (entry.Key == key)
            {
                return entry.Value;
            }
        }

        return null;
    }

    private void SetDiskDatabase(string key, string value)
    {
        foreach (var entry in _diskDataBase)
        {
            if (entry.Key == key)
            {
                entry.Value = value;
                return;
            }
        }

        _diskDataBase.Add(new DescriptorFileEntry(key, value, DescriptorFileEntryType.Quoted));
    }
    private void Load(Stream source)
    {
        var buffer = ArrayPool<byte>.Shared.Rent((int)MaxSize);

        try
        {
            var dataRead = source.Read(buffer, 0, (int)MaxSize);

            if (dataRead == 0)
            {
                throw new IOException("Unable to read data from stream to read DescriptorFile. This is likely not a VMDK file");
            }

            var isDescriptorFile = false;

            foreach (var descriptorLine in Encoding.UTF8.GetString(buffer, 0, dataRead).Split('\r', '\n', '\0'))
            {
                var trimmedLine = descriptorLine.Trim();

                if (trimmedLine.Length == 0)
                {
                    continue;
                }

                if (trimmedLine.StartsWith("# Disk DescriptorFile", StringComparison.OrdinalIgnoreCase))
                {
                    isDescriptorFile = true;
                }
                else if (trimmedLine.StartsWith("RW", StringComparison.OrdinalIgnoreCase)
                    || trimmedLine.StartsWith("RDONLY", StringComparison.OrdinalIgnoreCase)
                    || trimmedLine.StartsWith("NOACCESS", StringComparison.OrdinalIgnoreCase))
                {
                    Extents.Add(ExtentDescriptor.Parse(trimmedLine.AsMemory()));
                }
                else
                {
                    var entry = DescriptorFileEntry.Parse(trimmedLine.ToString());

                    if (entry.Key.StartsWith("ddb.", StringComparison.Ordinal))
                    {
                        _diskDataBase.Add(entry);
                    }
                    else
                    {
                        _header.Add(entry);
                    }
                }
            }

            if (!isDescriptorFile)
            {
                throw new IOException($"VMDK Descriptor not found in first {MaxSize} bytes of file. Please verify that you open a descriptor VMDK file and not an actual image file");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
