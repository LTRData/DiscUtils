//
// Copyright (c) 2008-2013, Kenneth Bell
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
using DiscUtils.Streams;

namespace DiscUtils.Vhdx;

/// <summary>
/// Detailed information about a VHDX file.
/// </summary>
public sealed class DiskImageFileInfo
{
    private readonly LogSequence _activeLogSequence;
    private readonly FileHeader _fileHeader;
    private readonly Metadata _metadata;
    private readonly RegionTable _regions;
    private readonly VhdxHeader _vhdxHeader1;
    private readonly VhdxHeader _vhdxHeader2;

    internal DiskImageFileInfo(FileHeader fileHeader, VhdxHeader vhdxHeader1, VhdxHeader vhdxHeader2,
                               RegionTable regions, Metadata metadata, LogSequence activeLogSequence)
    {
        _fileHeader = fileHeader;
        _vhdxHeader1 = vhdxHeader1;
        _vhdxHeader2 = vhdxHeader2;
        _regions = regions;
        _metadata = metadata;
        _activeLogSequence = activeLogSequence;
    }

    /// <summary>
    /// Gets the active header for the VHDX file.
    /// </summary>
    public HeaderInfo ActiveHeader
    {
        get
        {
            if (_vhdxHeader1 == null)
            {
                if (_vhdxHeader2 == null)
                {
                    return null;
                }

                return new HeaderInfo(_vhdxHeader2);
            }

            if (_vhdxHeader2 == null)
            {
                return new HeaderInfo(_vhdxHeader1);
            }

            return
                new HeaderInfo(_vhdxHeader1.SequenceNumber > _vhdxHeader2.SequenceNumber
                    ? _vhdxHeader1
                    : _vhdxHeader2);
        }
    }

    /// <summary>
    /// Gets the active log sequence for this VHDX file.
    /// </summary>
    public IEnumerable<LogEntryInfo> ActiveLogSequence
    {
        get
        {
            if (_activeLogSequence != null)
            {
                foreach (var entry in _activeLogSequence)
                {
                    yield return new LogEntryInfo(entry);
                }
            }
        }
    }

    /// <summary>
    /// Gets the block size of the VHDX file.
    /// </summary>
    public long BlockSize => _metadata.FileParameters.BlockSize;

    /// <summary>
    /// Gets the VHDX 'parser' that created the VHDX file.
    /// </summary>
    public string Creator => _fileHeader.Creator;

    /// <summary>
    /// Gets the logical size of the disk represented by the VHDX file.
    /// </summary>
    public long DiskSize => (long)_metadata.DiskSize;

    /// <summary>
    /// Gets the first header (by file location) of the VHDX file.
    /// </summary>
    public HeaderInfo FirstHeader => new(_vhdxHeader1);

    /// <summary>
    /// Gets a value indicating whether the VHDX file has a parent file (i.e. is a differencing file).
    /// </summary>
    public bool HasParent => (_metadata.FileParameters.Flags & FileParametersFlags.HasParent) != 0;

    /// <summary>
    /// Gets a value indicaticating if (the VHDX file is a fixed disk.
    /// </summary>
    public bool IsFixedDisk => (_metadata.FileParameters.Flags & FileParametersFlags.Fixed) != 0;

    /// <summary>
    /// Gets a value indicaticating if the VHDX file is a dynamic disk.
    /// </summary>
    public bool IsDynamicDisk => (_metadata.FileParameters.Flags & FileParametersFlags.Fixed) == 0;


    /// <summary>
    /// Gets the logical sector size of the disk represented by the VHDX file.
    /// </summary>
    public long LogicalSectorSize => _metadata.LogicalSectorSize;

    /// <summary>
    /// Gets the metadata table of the VHDX file.
    /// </summary>
    public MetadataTableInfo MetadataTable => new(_metadata.Table);

    /// <summary>
    /// Gets the set of parent locators, for differencing files.
    /// </summary>
    public IDictionary<string, string> ParentLocatorEntries => _metadata.ParentLocator != null
                ? _metadata.ParentLocator.Entries
                : [];

    /// <summary>
    /// Gets the parent locator type, for differencing files.
    /// </summary>
    public Guid ParentLocatorType => _metadata.ParentLocator != null ? _metadata.ParentLocator.LocatorType : Guid.Empty;

    /// <summary>
    /// Gets the physical sector size of disk represented by the VHDX file.
    /// </summary>
    public long PhysicalSectorSize => _metadata.PhysicalSectorSize;

    /// <summary>
    /// Gets the region table of the VHDX file.
    /// </summary>
    public RegionTableInfo RegionTable => new(_regions);

    /// <summary>
    /// Gets the second header (by file location) of the VHDX file.
    /// </summary>
    public HeaderInfo SecondHeader => new(_vhdxHeader2);

    /// <summary>
    /// Gets the file signature.
    /// </summary>
    public string Signature
    {
        get
        {
            Span<byte> buffer = stackalloc byte[8];
            EndianUtilities.WriteBytesLittleEndian(_fileHeader.Signature, buffer);

            return EncodingUtilities
                .GetLatin1Encoding()
                .GetString(buffer);
        }
    }
}