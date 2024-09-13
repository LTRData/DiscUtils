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
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Archives;
using DiscUtils.Streams;
using LTRData.Extensions.Formatting;

namespace DiscUtils.Xva;

/// <summary>
/// A class that can be used to create Xen Virtual Appliance (XVA) files.
/// </summary>
/// <remarks>This class is not intended to be a general purpose XVA generator,
/// the options to control the VM properties are strictly limited.  The class
/// generates a minimal VM really as a wrapper for one or more disk images, 
/// making them easy to import into XenServer.</remarks>
public sealed class VirtualMachineBuilder : StreamBuilder, IDisposable
{
    private readonly List<(string label, SparseStream content, Ownership ownsContent)> _disks;

    /// <summary>
    /// Initializes a new instance of the VirtualMachineBuilder class.
    /// </summary>
    public VirtualMachineBuilder()
    {
        _disks = [];
        DisplayName = "VM";
    }

    /// <summary>
    /// Gets or sets the display name of the VM.
    /// </summary>
    public string DisplayName { get; set; }

    /// <summary>
    /// Disposes this instance, including any underlying resources.
    /// </summary>
    public void Dispose()
    {
        foreach (var r in _disks)
        {
            if (r.ownsContent == Ownership.Dispose)
            {
                r.content.Dispose();
            }
        }
    }

    /// <summary>
    /// Adds a sparse disk image to the XVA file.
    /// </summary>
    /// <param name="label">The admin-visible name of the disk.</param>
    /// <param name="content">The content of the disk.</param>
    /// <param name="ownsContent">Indicates if ownership of content is transfered.</param>
    public void AddDisk(string label, SparseStream content, Ownership ownsContent)
    {
        _disks.Add((label, content, ownsContent));
    }

    /// <summary>
    /// Adds a disk image to the XVA file.
    /// </summary>
    /// <param name="label">The admin-visible name of the disk.</param>
    /// <param name="content">The content of the disk.</param>
    /// <param name="ownsContent">Indicates if ownership of content is transfered.</param>
    public void AddDisk(string label, Stream content, Ownership ownsContent)
    {
        _disks.Add((label, SparseStream.FromStream(content, ownsContent), Ownership.Dispose));
    }

    /// <summary>
    /// Creates a new stream that contains the XVA image.
    /// </summary>
    /// <returns>The new stream.</returns>
    public override Stream Build()
    {
        var tarBuilder = new TarFileBuilder();

        var ovaFileContent = GenerateOvaXml(out var diskIds);
        tarBuilder.AddFile("ova.xml", Encoding.ASCII.GetBytes(ovaFileContent));

        var diskIdx = 0;
        foreach (var diskRec in _disks)
        {
            var diskStream = diskRec.content;
            var extents = new List<StreamExtent>(diskStream.Extents);

            var lastChunkAdded = -1;
            foreach (var extent in extents)
            {
                var firstChunk = (int)(extent.Start / Sizes.OneMiB);
                var lastChunk = (int)((extent.Start + extent.Length - 1) / Sizes.OneMiB);

                for (var i = firstChunk; i <= lastChunk; ++i)
                {
                    if (i != lastChunkAdded)
                    {
                        Stream chunkStream;

                        var diskBytesLeft = diskStream.Length - i * Sizes.OneMiB;
                        if (diskBytesLeft < Sizes.OneMiB)
                        {
                            chunkStream = new ConcatStream(
                                Ownership.Dispose, [
                                new SubStream(diskStream, i * Sizes.OneMiB, diskBytesLeft),
                                new ZeroStream(Sizes.OneMiB - diskBytesLeft) ]);
                        }
                        else
                        {
                            chunkStream = new SubStream(diskStream, i * Sizes.OneMiB, Sizes.OneMiB);
                        }

                        Stream chunkHashStream;
                        var hashAlgCore = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
                        chunkHashStream = new HashStreamCore(chunkStream, Ownership.Dispose, hashAlgCore);

                        tarBuilder.AddFile(string.Format(CultureInfo.InvariantCulture, "Ref:{0}/{1:D8}", diskIds[diskIdx], i), chunkHashStream);

                        var hash = hashAlgCore.GetHashAndReset();

                        var hashString = hash.ToHexString();
                        var hashStringAscii = Encoding.ASCII.GetBytes(hashString);
                        tarBuilder.AddFile(string.Format(CultureInfo.InvariantCulture, "Ref:{0}/{1:D8}.checksum", diskIds[diskIdx], i), hashStringAscii);

                        lastChunkAdded = i;
                    }
                }
            }

            // Make sure the last chunk is present, filled with zero's if necessary
            var lastActualChunk = (int)((diskStream.Length - 1) / Sizes.OneMiB);
            if (lastChunkAdded < lastActualChunk)
            {
                Stream chunkStream = new ZeroStream(Sizes.OneMiB);

                Stream chunkHashStream;
                var hashAlgCore = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
                chunkHashStream = new HashStreamCore(chunkStream, Ownership.Dispose, hashAlgCore);

                tarBuilder.AddFile(string.Format(CultureInfo.InvariantCulture, "Ref:{0}/{1:D8}", diskIds[diskIdx], lastActualChunk), chunkHashStream);

                var hash = hashAlgCore.GetHashAndReset();

                var hashString = hash.ToHexString();
                var hashStringAscii = Encoding.ASCII.GetBytes(hashString);
                tarBuilder.AddFile(string.Format(CultureInfo.InvariantCulture, "Ref:{0}/{1:D8}.checksum", diskIds[diskIdx], lastActualChunk), hashStringAscii);
            }

            ++diskIdx;
        }

        return tarBuilder.Build();
    }

    /// <summary>
    /// Creates a new stream that contains the XVA image.
    /// </summary>
    /// <returns>The new stream.</returns>
    public override Task<Stream> BuildAsync(CancellationToken cancellationToken)
    {
        var tarBuilder = new TarFileBuilder();

        var ovaFileContent = GenerateOvaXml(out var diskIds);
        tarBuilder.AddFile("ova.xml", Encoding.ASCII.GetBytes(ovaFileContent));

        var diskIdx = 0;
        foreach (var diskRec in _disks)
        {
            var diskStream = diskRec.content;
            var extents = new List<StreamExtent>(diskStream.Extents);

            var lastChunkAdded = -1;
            foreach (var extent in extents)
            {
                var firstChunk = (int)(extent.Start / Sizes.OneMiB);
                var lastChunk = (int)((extent.Start + extent.Length - 1) / Sizes.OneMiB);

                for (var i = firstChunk; i <= lastChunk; ++i)
                {
                    if (i != lastChunkAdded)
                    {
                        Stream chunkStream;

                        var diskBytesLeft = diskStream.Length - i * Sizes.OneMiB;
                        if (diskBytesLeft < Sizes.OneMiB)
                        {
                            chunkStream = new ConcatStream(
                                Ownership.Dispose, [
                                new SubStream(diskStream, i * Sizes.OneMiB, diskBytesLeft),
                                new ZeroStream(Sizes.OneMiB - diskBytesLeft) ]);
                        }
                        else
                        {
                            chunkStream = new SubStream(diskStream, i * Sizes.OneMiB, Sizes.OneMiB);
                        }

                        var hashAlgCore = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
                        var chunkHashStream = new HashStreamCore(chunkStream, Ownership.Dispose, hashAlgCore);

                        tarBuilder.AddFile(string.Format(CultureInfo.InvariantCulture, "Ref:{0}/{1:D8}", diskIds[diskIdx], i), chunkHashStream);

                        var hash = hashAlgCore.GetHashAndReset();

                        var hashString = hash.ToHexString();
                        var hashStringAscii = Encoding.ASCII.GetBytes(hashString);
                        tarBuilder.AddFile(string.Format(CultureInfo.InvariantCulture, "Ref:{0}/{1:D8}.checksum", diskIds[diskIdx], i), hashStringAscii);

                        lastChunkAdded = i;
                    }
                }
            }

            // Make sure the last chunk is present, filled with zero's if necessary
            var lastActualChunk = (int)((diskStream.Length - 1) / Sizes.OneMiB);
            if (lastChunkAdded < lastActualChunk)
            {
                Stream chunkStream = new ZeroStream(Sizes.OneMiB);

                var hashAlgCore = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
                var chunkHashStream = new HashStreamCore(chunkStream, Ownership.Dispose, hashAlgCore);

                tarBuilder.AddFile(string.Format(CultureInfo.InvariantCulture, "Ref:{0}/{1:D8}", diskIds[diskIdx], lastActualChunk), chunkHashStream);

                var hash = hashAlgCore.GetHashAndReset();

                var hashString = hash.ToHexString();
                var hashStringAscii = Encoding.ASCII.GetBytes(hashString);
                tarBuilder.AddFile(string.Format(CultureInfo.InvariantCulture, "Ref:{0}/{1:D8}.checksum", diskIds[diskIdx], lastActualChunk), hashStringAscii);
            }

            ++diskIdx;
        }

        return tarBuilder.BuildAsync(cancellationToken);
    }

    protected override List<BuilderExtent> FixExtents(out long totalLength)
    {
        // Not required - deferred to TarFileBuilder
        throw new NotSupportedException();
    }

    private string GenerateOvaXml(out int[] diskIds)
    {
        var id = 0;

        var vmGuid = Guid.NewGuid();
        var vmName = DisplayName;
        var vmId = id++;

        // Establish per-disk info
        Span<Guid> vbdGuids = stackalloc Guid[_disks.Count];
        Span<int> vbdIds = stackalloc int[_disks.Count];
        Span<Guid> vdiGuids = stackalloc Guid[_disks.Count];
        var vdiNames = new string[_disks.Count];
        var vdiIds = new int[_disks.Count];
        Span<long> vdiSizes = stackalloc long[_disks.Count];

        var diskIdx = 0;
        foreach (var disk in _disks)
        {
            vbdGuids[diskIdx] = Guid.NewGuid();
            vbdIds[diskIdx] = id++;
            vdiGuids[diskIdx] = Guid.NewGuid();
            vdiIds[diskIdx] = id++;
            vdiNames[diskIdx] = disk.label;
            vdiSizes[diskIdx] = MathUtilities.RoundUp(disk.content.Length, Sizes.OneMiB);
            diskIdx++;
        }

        // Establish SR info
        var srGuid = Guid.NewGuid();
        var srName = "SR";
        var srId = id++;

        var vbdRefs = string.Empty;
        for (var i = 0; i < _disks.Count; ++i)
        {
            vbdRefs += string.Format(CultureInfo.InvariantCulture, StaticStrings.XVA_ova_ref, $"Ref:{vbdIds[i]}");
        }

        var vdiRefs = string.Empty;
        for (var i = 0; i < _disks.Count; ++i)
        {
            vdiRefs += string.Format(CultureInfo.InvariantCulture, StaticStrings.XVA_ova_ref, $"Ref:{vdiIds[i]}");
        }

        var objectsString = new StringBuilder();

        objectsString.Append(string.Format(CultureInfo.InvariantCulture, StaticStrings.XVA_ova_vm, $"Ref:{vmId}", vmGuid, vmName, vbdRefs));

        for (var i = 0; i < _disks.Count; ++i)
        {
            objectsString.Append(string.Format(CultureInfo.InvariantCulture, StaticStrings.XVA_ova_vbd, $"Ref:{vbdIds[i]}", vbdGuids[i], $"Ref:{vmId}", $"Ref:{vdiIds[i]}", i));
        }

        for (var i = 0; i < _disks.Count; ++i)
        {
            objectsString.Append(
                string.Format(
                    CultureInfo.InvariantCulture,
                    StaticStrings.XVA_ova_vdi,
                    $"Ref:{vdiIds[i]}",
                    vdiGuids[i],
                    vdiNames[i],
                    $"Ref:{srId}",
                    $"Ref:{vbdIds[i]}",
                    vdiSizes[i]));
        }

        objectsString.Append(string.Format(CultureInfo.InvariantCulture, StaticStrings.XVA_ova_sr, $"Ref:{srId}", srGuid, srName, vdiRefs));

        diskIds = vdiIds;
        return string.Format(CultureInfo.InvariantCulture, StaticStrings.XVA_ova_base, objectsString.ToString());
    }
}