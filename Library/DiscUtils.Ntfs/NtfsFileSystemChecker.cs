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
using System.Text;
using DiscUtils.Streams;

namespace DiscUtils.Ntfs;

/// <summary>
/// Class that checks NTFS file system integrity.
/// </summary>
/// <remarks>Poor relation of chkdsk/fsck.</remarks>
public sealed class NtfsFileSystemChecker : DiscFileSystemChecker
{
    private readonly Stream _target;

    private NtfsContext _context;
    private TextWriter _report;
    private ReportLevels _reportLevels;

    private ReportLevels _levelsDetected;
    private readonly ReportLevels _levelsConsideredFail = ReportLevels.Errors;

    /// <summary>
    /// Initializes a new instance of the NtfsFileSystemChecker class.
    /// </summary>
    /// <param name="diskData">The file system to check.</param>
    public NtfsFileSystemChecker(Stream diskData)
    {
        var protectiveStream = new SnapshotStream(diskData, Ownership.None);
        protectiveStream.Snapshot();
        protectiveStream.Freeze();
        _target = protectiveStream;
    }

    /// <summary>
    /// Checks the integrity of an NTFS file system held in a stream.
    /// </summary>
    /// <param name="reportOutput">A report on issues found.</param>
    /// <param name="levels">The amount of detail to report.</param>
    /// <returns><c>true</c> if the file system appears valid, else <c>false</c>.</returns>
    public override bool Check(TextWriter reportOutput, ReportLevels levels)
    {
        _context = new NtfsContext
        {
            RawStream = _target,
            Options = new NtfsOptions()
        };

        _report = reportOutput;
        _reportLevels = levels;
        _levelsDetected = ReportLevels.None;

        try
        {
            DoCheck();
        }
        catch (AbortException ae)
        {
            ReportError($"File system check aborted: {ae}");
            return false;
        }
        catch (Exception e)
        {
            ReportError($"File system check aborted with exception: {e}");
            return false;
        }

        return (_levelsDetected & _levelsConsideredFail) == 0;
    }

    /// <summary>
    /// Gets an object that can convert between clusters and files.
    /// </summary>
    /// <returns>The cluster map.</returns>
    public ClusterMap BuildClusterMap()
    {
        _context = new NtfsContext
        {
            RawStream = _target,
            Options = new NtfsOptions()
        };

        _context.RawStream.Position = 0;
        Span<byte> bytes = stackalloc byte[512];
        _context.RawStream.ReadExactly(bytes);

        _context.BiosParameterBlock = BiosParameterBlock.FromBytes(bytes);

        _context.Mft = new MasterFileTable(_context);
        var mftFile = new File(_context, _context.Mft.GetBootstrapRecord());
        _context.Mft.Initialize(mftFile);
        return _context.Mft.GetClusterMap();
    }

    private static void Abort()
    {
        throw new AbortException();
    }

    private void DoCheck()
    {
        _context.RawStream.Position = 0;
        Span<byte> bytes = stackalloc byte[512];
        _context.RawStream.ReadExactly(bytes);

        _context.BiosParameterBlock = BiosParameterBlock.FromBytes(bytes);

        //-----------------------------------------------------------------------
        // MASTER FILE TABLE
        //

        // Bootstrap the Master File Table
        _context.Mft = new MasterFileTable(_context);
        var mftFile = new File(_context, _context.Mft.GetBootstrapRecord());

        // Verify basic MFT records before initializing the Master File Table
        PreVerifyMft(mftFile);
        _context.Mft.Initialize(mftFile);

        // Now the MFT is up and running, do more detailed analysis of it's contents - double-accounted clusters, etc
        VerifyMft();
        _context.Mft.Dump(_report, "INFO: ");

        //-----------------------------------------------------------------------
        // INDEXES
        //

        // Need UpperCase in order to verify some indexes (i.e. directories).
        var ucFile = new File(_context, _context.Mft.GetRecord(MasterFileTable.UpCaseIndex, false));
        _context.UpperCase = new UpperCase(ucFile);

        SelfCheckIndexes();

        //-----------------------------------------------------------------------
        // DIRECTORIES
        //
        VerifyDirectories();

        //-----------------------------------------------------------------------
        // WELL KNOWN FILES
        //
        VerifyWellKnownFilesExist();

        //-----------------------------------------------------------------------
        // OBJECT IDS
        //
        VerifyObjectIds();

        //-----------------------------------------------------------------------
        // FINISHED
        //

        // Temporary...
        using var fs = new NtfsFileSystem(_context.RawStream);
        if ((_reportLevels & ReportLevels.Information) != 0)
        {
            ReportDump(fs);
        }
    }

    private void VerifyWellKnownFilesExist()
    {
        var rootDir = new Directory(_context, _context.Mft.GetRecord(MasterFileTable.RootDirIndex, false));

        var extendDirEntry = rootDir.GetEntryByName("$Extend");
        if (extendDirEntry == null)
        {
            ReportError("$Extend does not exist in root directory");
            Abort();
        }

        var extendDir = new Directory(_context, _context.Mft.GetRecord(extendDirEntry.Value.Reference));

        var objIdDirEntry = extendDir.GetEntryByName("$ObjId");
        if (objIdDirEntry == null)
        {
            ReportError("$ObjId does not exist in $Extend directory");
            Abort();
        }

        // Stash ObjectIds
        _context.ObjectIds = new ObjectIds(new File(_context, _context.Mft.GetRecord(objIdDirEntry.Value.Reference)));

        var sysVolInfDirEntry = rootDir.GetEntryByName("System Volume Information");
        if (sysVolInfDirEntry == null)
        {
            ReportError("'System Volume Information' does not exist in root directory");
            Abort();
        }
        ////Directory sysVolInfDir = new Directory(_context, _context.Mft.GetRecord(sysVolInfDirEntry.Reference));
    }

    private void VerifyObjectIds()
    {
        foreach (var fr in _context.Mft.Records)
        {
            if (fr.BaseFile.Value != 0)
            {
                var f = new File(_context, fr);
                foreach (var stream in f.AllStreams)
                {
                    if (stream.AttributeType == AttributeType.ObjectId)
                    {
                        var objId = stream.GetContent<ObjectId>();
                        if (!_context.ObjectIds.TryGetValue(objId.Id, out var objIdRec))
                        {
                            ReportError("ObjectId {0} for file {1} is not indexed", objId.Id, f.BestName);
                        }
                        else if (objIdRec.MftReference != f.MftReference)
                        {
                            ReportError("ObjectId {0} for file {1} points to {2}", objId.Id, f.BestName,
                                objIdRec.MftReference);
                        }
                    }
                }
            }
        }

        foreach (var objIdRec in _context.ObjectIds.All)
        {
            if (_context.Mft.GetRecord(objIdRec.Value.MftReference) == null)
            {
                ReportError("ObjectId {0} refers to non-existant file {1}", objIdRec.Key,
                    objIdRec.Value.MftReference);
            }
        }
    }

    private void VerifyDirectories()
    {
        foreach (var fr in _context.Mft.Records)
        {
            if (fr.BaseFile.Value != 0)
            {
                continue;
            }

            var f = new File(_context, fr);
            foreach (var stream in f.AllStreams)
            {
                if (stream.AttributeType == AttributeType.IndexRoot && stream.Name == "$I30")
                {
                    var dir =
                        new IndexView<FileNameRecord, FileRecordReference>(f.GetIndex("$I30"));
                    foreach (var entry in dir.Entries)
                    {
                        var refFile = _context.Mft.GetRecord(entry.Value);

                        // Make sure each referenced file actually exists...
                        if (refFile == null)
                        {
                            ReportError("Directory {0} references non-existent file {1}", f, entry.Key);
                        }

                        var referencedFile = new File(_context, refFile);
                        var si = referencedFile.StandardInformation;
                        if (si.CreationTime != entry.Key.CreationTime ||
                            si.MftChangedTime != entry.Key.MftChangedTime
                            || si.ModificationTime != entry.Key.ModificationTime)
                        {
                            ReportInfo("Directory entry {0} in {1} is out of date", entry.Key, f);
                        }
                    }
                }
            }
        }
    }

    private void SelfCheckIndexes()
    {
        foreach (var fr in _context.Mft.Records)
        {
            var f = new File(_context, fr);
            foreach (var stream in f.AllStreams)
            {
                if (stream.AttributeType == AttributeType.IndexRoot)
                {
                    SelfCheckIndex(f, stream.Name);
                }
            }
        }
    }

    private void SelfCheckIndex(File file, string name)
    {
        ReportInfo("About to self-check index {0} in file {1} (MFT:{2})", name, file.BestName, file.IndexInMft);

        var root = file.GetStream(AttributeType.IndexRoot, name).Value.GetContent<IndexRoot>();

        byte[] rootBuffer;
        using (var s = file.OpenStream(AttributeType.IndexRoot, name, FileAccess.Read))
        {
            rootBuffer = s.ReadExactly((int)s.Length);
        }

        Bitmap indexBitmap = null;
        if (file.GetStream(AttributeType.Bitmap, name) != null)
        {
            indexBitmap = new Bitmap(file.OpenStream(AttributeType.Bitmap, name, FileAccess.Read), long.MaxValue);
        }

        if (!SelfCheckIndexNode(rootBuffer.AsSpan(IndexRoot.HeaderOffset), indexBitmap, root, file.BestName, name))
        {
            ReportError("Index {0} in file {1} (MFT:{2}) has corrupt IndexRoot attribute", name, file.BestName,
                file.IndexInMft);
        }
        else
        {
            ReportInfo("Self-check of index {0} in file {1} (MFT:{2}) complete", name, file.BestName,
                file.IndexInMft);
        }
    }

    private bool SelfCheckIndexNode(ReadOnlySpan<byte> buffer, Bitmap bitmap, IndexRoot root, string fileName,
                                    string indexName)
    {
        var ok = true;

        var header = new IndexHeader(buffer);

        IndexEntry lastEntry = null;

        var collator = root.GetCollator(_context.UpperCase);

        var pos = (int)header.OffsetToFirstEntry;
        while (pos < header.TotalSizeOfEntries)
        {
            var entry = new IndexEntry(indexName == "$I30");
            entry.Read(buffer.Slice(pos));
            pos += entry.Size;

            if ((entry.Flags & IndexEntryFlags.Node) != 0)
            {
                var bitmapIdx = entry.ChildrenVirtualCluster /
                                 MathUtilities.Ceil(root.IndexAllocationSize,
                                     _context.BiosParameterBlock.SectorsPerCluster *
                                     _context.BiosParameterBlock.BytesPerSector);
                if (!bitmap.IsPresent(bitmapIdx))
                {
                    ReportError("Index entry {0} is non-leaf, but child vcn {1} is not in bitmap at index {2}",
                        Index.EntryAsString(entry, fileName, indexName), entry.ChildrenVirtualCluster, bitmapIdx);
                }
            }

            if ((entry.Flags & IndexEntryFlags.End) != 0)
            {
                if (pos != header.TotalSizeOfEntries)
                {
                    ReportError("Found END index entry {0}, but not at end of node",
                        Index.EntryAsString(entry, fileName, indexName));
                    ok = false;
                }
            }

            if (lastEntry != null && collator.Compare(lastEntry.KeyBuffer, entry.KeyBuffer) >= 0)
            {
                ReportError("Found entries out of order {0} was before {1}",
                    Index.EntryAsString(lastEntry, fileName, indexName),
                    Index.EntryAsString(entry, fileName, indexName));
                ok = false;
            }

            lastEntry = entry;
        }

        return ok;
    }

    private void PreVerifyMft(File file)
    {
        var recordLength = _context.BiosParameterBlock.MftRecordSize;
        int bytesPerSector = _context.BiosParameterBlock.BytesPerSector;

        // Check out the MFT's clusters
        foreach (var range in file.GetAttribute(AttributeType.Data, null).GetClusters())
        {
            if (!VerifyClusterRange(range))
            {
                ReportError("Corrupt cluster range in MFT data attribute {0}", range.ToString());
                Abort();
            }
        }

        foreach (var range in file.GetAttribute(AttributeType.Bitmap, null).GetClusters())
        {
            if (!VerifyClusterRange(range))
            {
                ReportError("Corrupt cluster range in MFT bitmap attribute {0}", range.ToString());
                Abort();
            }
        }

        using Stream mftStream = file.OpenStream(AttributeType.Data, null, FileAccess.Read);
        using Stream bitmapStream = file.OpenStream(AttributeType.Bitmap, null, FileAccess.Read);
        var bitmap = new Bitmap(bitmapStream, long.MaxValue);

        long index = 0;

        var recordData = new byte[recordLength];

        while (mftStream.Position < mftStream.Length)
        {
            mftStream.ReadExactly(recordData);

            var magic = EndianUtilities.BytesToString(recordData, 0, 4);
            if (magic != "FILE")
            {
                if (bitmap.IsPresent(index))
                {
                    ReportError("Invalid MFT record magic at index {0} - was ({2},{3},{4},{5}) \"{1}\"", index,
                        magic.Trim('\0'), (int)magic[0], (int)magic[1], (int)magic[2], (int)magic[3]);
                }
            }
            else
            {
                if (!VerifyMftRecord(recordData, bitmap.IsPresent(index), bytesPerSector))
                {
                    ReportError("Invalid MFT record at index {0}", index);
                    var bldr = new StringBuilder();
                    for (var i = 0; i < recordData.Length; ++i)
                    {
                        bldr.Append($" {recordData[i]:X2}");
                    }

                    ReportInfo("MFT record binary data for index {0}:{1}", index, bldr.ToString());
                }
            }

            index++;
        }
    }

    private void VerifyMft()
    {
        // Cluster allocation check - check for double allocations
        var clusterMap = new Dictionary<long, string>();
        foreach (var fr in _context.Mft.Records)
        {
            if ((fr.Flags & FileRecordFlags.InUse) != 0)
            {
                var f = new File(_context, fr);
                foreach (var attr in f.AllAttributes)
                {
                    var attrKey = $"{fr.MasterFileTableIndex}:{attr.Id}";

                    foreach (var range in attr.GetClusters())
                    {
                        if (!VerifyClusterRange(range))
                        {
                            ReportError("Attribute {0} contains bad cluster range {1}", attrKey, range);
                        }

                        for (var cluster = range.Offset; cluster < range.Offset + range.Count; ++cluster)
                        {
                            if (clusterMap.TryGetValue(cluster, out var existingKey))
                            {
                                ReportError(
                                    "Two attributes referencing cluster {0} (0x{0:X16}) - {1} and {2} (as MftIndex:AttrId)",
                                    cluster, existingKey, attrKey);
                            }
                        }
                    }
                }
            }
        }
    }

    private bool VerifyMftRecord(byte[] recordData, bool presentInBitmap, int bytesPerSector)
    {
        var ok = true;

        var genericRecord = new GenericFixupRecord(bytesPerSector);

        //
        // Verify the attributes seem OK...
        //
        var tempBuffer = ArrayPool<byte>.Shared.Rent(recordData.Length);
        try
        {
            System.Buffer.BlockCopy(recordData, 0, tempBuffer, 0, recordData.Length);
            genericRecord.FromBytes(tempBuffer.AsSpan(0, recordData.Length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(tempBuffer);
        }

        int pos = EndianUtilities.ToUInt16LittleEndian(genericRecord.Content, 0x14);

        while (EndianUtilities.ToUInt32LittleEndian(genericRecord.Content, pos) != 0xFFFFFFFF)
        {
            int attrLen;
            try
            {
                var ar = AttributeRecord.FromBytes(genericRecord.Content.AsSpan(pos), out attrLen);
                if (attrLen != ar.Size)
                {
                    ReportError("Attribute size is different to calculated size.  AttrId={0}", ar.AttributeId);
                    ok = false;
                }

                if (ar.IsNonResident)
                {
                    var nrr = (NonResidentAttributeRecord)ar;
                    if (nrr.DataRuns.Count > 0)
                    {
                        long totalVcn = 0;
                        foreach (var run in nrr.DataRuns)
                        {
                            totalVcn += run.RunLength;
                        }

                        if (totalVcn != nrr.LastVcn - nrr.StartVcn + 1)
                        {
                            ReportError("Declared VCNs doesn't match data runs.  AttrId={0}", ar.AttributeId);
                            ok = false;
                        }
                    }
                }
            }
            catch
            {
                ReportError("Failure parsing attribute at pos={0}", pos);
                return false;
            }

            pos += attrLen;
        }

        //
        // Now consider record as a whole
        //
        var record = new FileRecord(bytesPerSector);
        record.FromBytes(recordData);

        var inUse = (record.Flags & FileRecordFlags.InUse) != 0;
        if (inUse != presentInBitmap)
        {
            ReportError("MFT bitmap and record in-use flag don't agree.  Mft={0}, Record={1}",
                presentInBitmap ? "InUse" : "Free", inUse ? "InUse" : "Free");
            ok = false;
        }

        if (record.Size != record.RealSize)
        {
            ReportError("MFT record real size is different to calculated size.  Stored in MFT={0}, Calculated={1}",
                record.RealSize, record.Size);
            ok = false;
        }

        if (EndianUtilities.ToUInt32LittleEndian(recordData, (int)record.RealSize - 8) != uint.MaxValue)
        {
            ReportError("MFT record is not correctly terminated with 0xFFFFFFFF");
            ok = false;
        }

        return ok;
    }

    private bool VerifyClusterRange(Range<long, long> range)
    {
        var ok = true;
        if (range.Offset < 0)
        {
            ReportError("Invalid cluster range {0} - negative start", range);
            ok = false;
        }

        if (range.Count <= 0)
        {
            ReportError("Invalid cluster range {0} - negative/zero count", range);
            ok = false;
        }

        if ((range.Offset + range.Count) * _context.BiosParameterBlock.BytesPerCluster > _context.RawStream.Length)
        {
            ReportError("Invalid cluster range {0} - beyond end of disk", range);
            ok = false;
        }

        return ok;
    }

    private void ReportDump(IDiagnosticTraceable toDump)
    {
        _levelsDetected |= ReportLevels.Information;
        if ((_reportLevels & ReportLevels.Information) != 0)
        {
            toDump.Dump(_report, "INFO: ");
        }
    }

    private void ReportInfo(string str, params object[] args)
    {
        _levelsDetected |= ReportLevels.Information;
        if ((_reportLevels & ReportLevels.Information) != 0)
        {
            _report.WriteLine($"INFO: {str}", args);
        }
    }

    private void ReportError(string str, params object[] args)
    {
        _levelsDetected |= ReportLevels.Errors;
        if ((_reportLevels & ReportLevels.Errors) != 0)
        {
            _report.WriteLine($"ERROR: {str}", args);
        }
    }

    private sealed class AbortException : InvalidFileSystemException
    {

    }
}