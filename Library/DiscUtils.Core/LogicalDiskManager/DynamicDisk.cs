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
using System.IO;
using DiscUtils.Partitions;
using DiscUtils.Streams;

namespace DiscUtils.LogicalDiskManager;

internal class DynamicDisk : IDiagnosticTraceable
{
    private readonly VirtualDisk _disk;
    private readonly PrivateHeader _header;

    internal DynamicDisk(VirtualDisk disk)
    {
        _disk = disk;
        _header = GetPrivateHeader(_disk);

        var toc = GetTableOfContents();

        var dbStart = _header.ConfigurationStartLba * 512 + toc.Item1Start * 512;
        _disk.Content.Position = dbStart;
        Database = new Database(_disk.Content);
    }

    public SparseStream Content => _disk.Content;

    public Database Database { get; }

    public long DataOffset => _header.DataStartLba;

    public Guid GroupId => string.IsNullOrEmpty(_header.DiskGroupId) ? Guid.Empty : new Guid(_header.DiskGroupId);

    public Guid Id => new(_header.DiskId);

    public void Dump(TextWriter writer, string linePrefix)
    {
        writer.WriteLine($"{linePrefix}DISK ({_header.DiskId})");
        writer.WriteLine($"{linePrefix}      Metadata Version: {(_header.Version >> 16) & 0xFFFF}.{_header.Version & 0xFFFF}");
        writer.WriteLine($"{linePrefix}             Timestamp: {_header.Timestamp}");
        writer.WriteLine($"{linePrefix}               Disk Id: {_header.DiskId}");
        writer.WriteLine($"{linePrefix}               Host Id: {_header.HostId}");
        writer.WriteLine($"{linePrefix}         Disk Group Id: {_header.DiskGroupId}");
        writer.WriteLine($"{linePrefix}       Disk Group Name: {_header.DiskGroupName}");
        writer.WriteLine($"{linePrefix}            Data Start: {_header.DataStartLba} (Sectors)");
        writer.WriteLine($"{linePrefix}             Data Size: {_header.DataSizeLba} (Sectors)");
        writer.WriteLine($"{linePrefix}   Configuration Start: {_header.ConfigurationStartLba} (Sectors)");
        writer.WriteLine($"{linePrefix}    Configuration Size: {_header.ConfigurationSizeLba} (Sectors)");
        writer.WriteLine($"{linePrefix}              TOC Size: {_header.TocSizeLba} (Sectors)");
        writer.WriteLine($"{linePrefix}              Next TOC: {_header.NextTocLba} (Sectors)");
        writer.WriteLine($"{linePrefix}     Number of Configs: {_header.NumberOfConfigs}");
        writer.WriteLine($"{linePrefix}           Config Size: {_header.ConfigurationSizeLba} (Sectors)");
        writer.WriteLine($"{linePrefix}        Number of Logs: {_header.NumberOfLogs}");
        writer.WriteLine($"{linePrefix}              Log Size: {_header.LogSizeLba} (Sectors)");
    }

    internal static PrivateHeader GetPrivateHeader(VirtualDisk disk)
    {
        if (disk.IsPartitioned)
        {
            long headerPos = 0;
            var pt = disk.Partitions;
            if (pt is BiosPartitionTable)
            {
                headerPos = 0xc00;
            }
            else
            {
                foreach (var part in pt.Partitions)
                {
                    if (part.GuidType == GuidPartitionTypes.WindowsLdmMetadata)
                    {
                        headerPos = part.LastSector * Sizes.Sector;
                    }
                }
            }

            if (headerPos != 0)
            {
                disk.Content.Position = headerPos;
                Span<byte> buffer = stackalloc byte[Sizes.Sector];
                buffer = buffer.Slice(0, disk.Content.Read(buffer));

                var hdr = new PrivateHeader();
                hdr.ReadFrom(buffer);
                return hdr;
            }
        }

        return null;
    }

    private TocBlock GetTableOfContents()
    {
        var buffer = new byte[_header.TocSizeLba * 512];
        _disk.Content.Position = _header.ConfigurationStartLba * 512 + 1 * _header.TocSizeLba * 512;

        _disk.Content.ReadExactly(buffer, 0, buffer.Length);
        var tocBlock = new TocBlock();
        tocBlock.ReadFrom(buffer);

        if (tocBlock.Signature == "TOCBLOCK")
        {
            return tocBlock;
        }

        return null;
    }
}