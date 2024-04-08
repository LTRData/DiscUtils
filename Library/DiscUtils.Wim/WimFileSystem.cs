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
using DiscUtils.Core.WindowsSecurity.AccessControl;
using DiscUtils.Internal;
using DiscUtils.Streams;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Linq;
using DiscUtils.Streams.Compatibility;
using LTRData.Extensions.Split;

namespace DiscUtils.Wim;

/// <summary>
/// Provides access to the file system within a WIM file image.
/// </summary>
public class WimFileSystem : ReadOnlyDiscFileSystem, IWindowsFileSystem
{
    private readonly ObjectCache<long, List<DirectoryEntry>> _dirCache;
    private WimFile _file;
    private Stream _metaDataStream;
    private long _rootDirPos;
    private List<RawSecurityDescriptor> _securityDescriptors;

    internal WimFileSystem(WimFile file, int index)
    {
        _file = file;

        var metaDataFileInfo = _file.LocateImage(index);
        if (metaDataFileInfo == null)
        {
            throw new ArgumentException($"No such image: {index}", nameof(index));
        }

        _metaDataStream = _file.OpenResourceStream(metaDataFileInfo);
        ReadSecurityDescriptors();

        _dirCache = new ObjectCache<long, List<DirectoryEntry>>();

        VolumeLabel = XDocument.Parse(_file.Manifest)?.XPathSelectElement($"WIM/IMAGE[@INDEX=\"{index + 1}\"]/NAME")?.Value;
    }

    public override string VolumeLabel { get; }

    /// <summary>
    /// Provides a friendly description of the file system type.
    /// </summary>
    public override string FriendlyName
    {
        get { return "Microsoft WIM"; }
    }

    /// <summary>
    /// Gets the security descriptor associated with the file or directory.
    /// </summary>
    /// <param name="path">The file or directory to inspect.</param>
    /// <returns>The security descriptor.</returns>
    public RawSecurityDescriptor GetSecurity(string path)
    {
        var id = GetEntry(path).SecurityId;

        if (id == uint.MaxValue)
        {
            return null;
        }

        if (id >= 0 && id < _securityDescriptors.Count)
        {
            return _securityDescriptors[(int)id];
        }

        // What if there is no descriptor?
        throw new NotImplementedException();
    }

    /// <summary>
    /// Sets the security descriptor associated with the file or directory.
    /// </summary>
    /// <param name="path">The file or directory to change.</param>
    /// <param name="securityDescriptor">The new security descriptor.</param>
    public void SetSecurity(string path, RawSecurityDescriptor securityDescriptor)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Gets the reparse point data associated with a file or directory.
    /// </summary>
    /// <param name="path">The file to query.</param>
    /// <returns>The reparse point information.</returns>
    public ReparsePoint GetReparsePoint(string path)
    {
        var dirEntry = GetEntry(path);

        var hdr = _file.LocateResource(dirEntry.Hash);
        if (hdr == null)
        {
            throw new IOException("No reparse point");
        }

        using Stream s = _file.OpenResourceStream(hdr);
        var buffer = new byte[s.Length];
        s.ReadExactly(buffer, 0, buffer.Length);
        return new ReparsePoint((int)dirEntry.ReparseTag, buffer);
    }

    /// <summary>
    /// Sets the reparse point data on a file or directory.
    /// </summary>
    /// <param name="path">The file to set the reparse point on.</param>
    /// <param name="reparsePoint">The new reparse point.</param>
    public void SetReparsePoint(string path, ReparsePoint reparsePoint)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Removes a reparse point from a file or directory, without deleting the file or directory.
    /// </summary>
    /// <param name="path">The path to the file or directory to remove the reparse point from.</param>
    public void RemoveReparsePoint(string path)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Gets the short name for a given path.
    /// </summary>
    /// <param name="path">The path to convert.</param>
    /// <returns>The short name.</returns>
    /// <remarks>
    /// This method only gets the short name for the final part of the path, to
    /// convert a complete path, call this method repeatedly, once for each path
    /// segment.  If there is no short name for the given path,<c>null</c> is
    /// returned.
    /// </remarks>
    public string GetShortName(string path)
    {
        var dirEntry = GetEntry(path);
        return dirEntry.ShortName;
    }

    /// <summary>
    /// Sets the short name for a given file or directory.
    /// </summary>
    /// <param name="path">The full path to the file or directory to change.</param>
    /// <param name="shortName">The shortName, which should not include a path.</param>
    public void SetShortName(string path, string shortName)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Gets the standard file information for a file.
    /// </summary>
    /// <param name="path">The full path to the file or directory to query.</param>
    /// <returns>The standard file information.</returns>
    public WindowsFileInformation GetFileStandardInformation(string path)
    {
        var dirEntry = GetEntry(path);

        return new WindowsFileInformation
        {
            CreationTime = DateTime.FromFileTimeUtc(dirEntry.CreationTime),
            LastAccessTime = DateTime.FromFileTimeUtc(dirEntry.LastAccessTime),
            ChangeTime =
                DateTime.FromFileTimeUtc(Math.Max(dirEntry.LastWriteTime,
                    Math.Max(dirEntry.CreationTime, dirEntry.LastAccessTime))),
            LastWriteTime = DateTime.FromFileTimeUtc(dirEntry.LastWriteTime),
            FileAttributes = dirEntry.Attributes
        };
    }

    /// <summary>
    /// Sets the standard file information for a file.
    /// </summary>
    /// <param name="path">The full path to the file or directory to query.</param>
    /// <param name="info">The standard file information.</param>
    public void SetFileStandardInformation(string path, WindowsFileInformation info)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Gets the names of the alternate data streams for a file.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <returns>
    /// The list of alternate data streams (or empty, if none).  To access the contents
    /// of the alternate streams, use OpenFile(path + ":" + name, ...).
    /// </returns>
    public IEnumerable<string> GetAlternateDataStreams(string path)
    {
        var dirEntry = GetEntry(path);

        if (dirEntry.AlternateStreams != null)
        {
            foreach (var altStream in dirEntry.AlternateStreams)
            {
                if (!string.IsNullOrEmpty(altStream.Name))
                {
                    yield return altStream.Name;
                }
            }
        }
    }

    /// <summary>
    /// Gets the file id for a given path.
    /// </summary>
    /// <param name="path">The path to get the id of.</param>
    /// <returns>The file id, or -1.</returns>
    /// <remarks>
    /// The returned file id uniquely identifies the file, and is shared by all hard
    /// links to the same file.  The value -1 indicates no unique identifier is
    /// available, and so it can be assumed the file has no hard links.
    /// </remarks>
    public long GetFileId(string path)
    {
        var dirEntry = GetEntry(path);
        return BitConverter.ToInt64(dirEntry.Hash, 0)
            ^ BitConverter.ToInt64(dirEntry.Hash, 8)
            ^ BitConverter.ToInt32(dirEntry.Hash, 16);
    }

    /// <summary>
    /// Indicates whether the file is known by other names.
    /// </summary>
    /// <param name="path">The file to inspect.</param>
    /// <returns><c>true</c> if the file has other names, else <c>false</c>.</returns>
    public bool HasHardLinks(string path)
    {
        var dirEntry = GetEntry(path);
        return dirEntry.HardLink != 0u;
    }

    public int GetHardLinkCount(string path)
    {
        return HasHardLinks(path) ? 2 : 1;
    }

    /// <summary>
    /// Indicates if a directory exists.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <returns>true if the directory exists.</returns>
    public override bool DirectoryExists(string path)
    {
        var dirEntry = GetEntry(path);
        return dirEntry != null && (dirEntry.Attributes & FileAttributes.Directory) != 0;
    }

    /// <summary>
    /// Indicates if a file exists.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <returns>true if the file exists.</returns>
    public override bool FileExists(string path)
    {
        var dirEntry = GetEntry(path);
        return dirEntry != null && (dirEntry.Attributes & FileAttributes.Directory) == 0;
    }

    /// <summary>
    /// Gets the names of subdirectories in a specified directory matching a specified
    /// search pattern, using a value to determine whether to search subdirectories.
    /// </summary>
    /// <param name="path">The path to search.</param>
    /// <param name="searchPattern">The search string to match against.</param>
    /// <param name="searchOption">Indicates whether to search subdirectories.</param>
    /// <returns>Array of directories matching the search pattern.</returns>
    public override IEnumerable<string> GetDirectories(string path, string searchPattern, SearchOption searchOption)
    {
        var filter = Utilities.ConvertWildcardsToRegEx(searchPattern, ignoreCase: true);

        var dirs = DoSearch(path, filter, searchOption == SearchOption.AllDirectories, true, false);
        return dirs;
    }

    /// <summary>
    /// Gets the names of files in a specified directory matching a specified
    /// search pattern, using a value to determine whether to search subdirectories.
    /// </summary>
    /// <param name="path">The path to search.</param>
    /// <param name="searchPattern">The search string to match against.</param>
    /// <param name="searchOption">Indicates whether to search subdirectories.</param>
    /// <returns>Array of files matching the search pattern.</returns>
    public override IEnumerable<string> GetFiles(string path, string searchPattern, SearchOption searchOption)
    {
        var re = Utilities.ConvertWildcardsToRegEx(searchPattern, ignoreCase: true);

        var results = DoSearch(path, re, searchOption == SearchOption.AllDirectories, false, true);
        return results;
    }

    /// <summary>
    /// Gets the names of all files and subdirectories in a specified directory.
    /// </summary>
    /// <param name="path">The path to search.</param>
    /// <returns>Array of files and subdirectories matching the search pattern.</returns>
    public override IEnumerable<string> GetFileSystemEntries(string path)
    {
        var parentDirEntry = GetEntry(path);
        if (parentDirEntry == null)
        {
            throw new DirectoryNotFoundException($"The directory '{path}' does not exist");
        }

        var parentDir = GetDirectory(parentDirEntry.SubdirOffset);

        return parentDir.Select(m => Utilities.CombinePaths(path, m.FileName));
    }

    /// <summary>
    /// Gets the names of files and subdirectories in a specified directory matching a specified
    /// search pattern.
    /// </summary>
    /// <param name="path">The path to search.</param>
    /// <param name="searchPattern">The search string to match against.</param>
    /// <returns>Array of files and subdirectories matching the search pattern.</returns>
    public override IEnumerable<string> GetFileSystemEntries(string path, string searchPattern)
    {
        var filter = Utilities.ConvertWildcardsToRegEx(searchPattern, ignoreCase: true);

        var parentDirEntry = GetEntry(path);
        if (parentDirEntry == null)
        {
            throw new DirectoryNotFoundException($"The directory '{path}' does not exist");
        }

        var parentDir = GetDirectory(parentDirEntry.SubdirOffset);

        var result = parentDir
            .Where(dirEntry => filter is null || filter(dirEntry.FileName))
            .Select(dirEntry => Utilities.CombinePaths(path, dirEntry.FileName));

        return result;
    }

    /// <summary>
    /// Opens the specified file.
    /// </summary>
    /// <param name="path">The full path of the file to open.</param>
    /// <param name="mode">The file mode for the created stream.</param>
    /// <param name="access">The access permissions for the created stream.</param>
    /// <returns>The new stream.</returns>
    public override SparseStream OpenFile(string path, FileMode mode, FileAccess access)
    {
        if (mode != FileMode.Open && mode != FileMode.OpenOrCreate)
        {
            throw new NotSupportedException("No write support for WIM files");
        }

        if (access != FileAccess.Read)
        {
            throw new NotSupportedException("No write support for WIM files");
        }

        var streamHash = GetFileHash(path);
        var hdr = _file.LocateResource(streamHash);
        if (hdr == null)
        {
            if (Utilities.IsAllZeros(streamHash, 0, streamHash.Length))
            {
                return new ZeroStream(0);
            }

            throw new IOException("Unable to locate file contents");
        }

        return _file.OpenResourceStream(hdr);
    }

    /// <summary>
    /// Gets the attributes of a file or directory.
    /// </summary>
    /// <param name="path">The file or directory to inspect.</param>
    /// <returns>The attributes of the file or directory.</returns>
    public override FileAttributes GetAttributes(string path)
    {
        var dirEntry = GetEntry(path);
        if (dirEntry == null)
        {
            throw new FileNotFoundException("No such file or directory", path);
        }

        return dirEntry.Attributes;
    }

    /// <summary>
    /// Gets the creation time (in UTC) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <returns>The creation time.</returns>
    public override DateTime GetCreationTimeUtc(string path)
    {
        var dirEntry = GetEntry(path);
        if (dirEntry == null)
        {
            throw new FileNotFoundException("No such file or directory", path);
        }

        return DateTime.FromFileTimeUtc(dirEntry.CreationTime);
    }

    /// <summary>
    /// Gets the last access time (in UTC) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <returns>The last access time.</returns>
    public override DateTime GetLastAccessTimeUtc(string path)
    {
        var dirEntry = GetEntry(path);
        if (dirEntry == null)
        {
            throw new FileNotFoundException("No such file or directory", path);
        }

        return DateTime.FromFileTimeUtc(dirEntry.LastAccessTime);
    }

    /// <summary>
    /// Gets the last modification time (in UTC) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <returns>The last write time.</returns>
    public override DateTime GetLastWriteTimeUtc(string path)
    {
        var dirEntry = GetEntry(path);
        if (dirEntry == null)
        {
            throw new FileNotFoundException("No such file or directory", path);
        }

        return DateTime.FromFileTimeUtc(dirEntry.LastWriteTime);
    }

    /// <summary>
    /// Gets the length of a file.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <returns>The length in bytes.</returns>
    public override long GetFileLength(string path)
    {
        var streamHash = GetFileHash(path);
        var hdr = _file.LocateResource(streamHash);
        if (hdr == null)
        {
            if (Utilities.IsAllZeros(streamHash, 0, streamHash.Length))
            {
                return 0;
            }

            throw new IOException("Unable to locate file contents");
        }

        return hdr.OriginalSize;
    }

    public override DiscFileSystemInfo GetFileSystemInfo(string path)
    {
        var dirEntry = GetEntry(path);
        if (dirEntry == null)
        {
            return new(this, path);
        }

        if (dirEntry.Attributes.HasFlag(FileAttributes.Directory))
        {
            return new CachedDiscDirectoryInfo(this, path, dirEntry.Attributes, DateTime.FromFileTimeUtc(dirEntry.CreationTime),
                                            DateTime.FromFileTimeUtc(dirEntry.LastAccessTime),
                                            DateTime.FromFileTimeUtc(dirEntry.LastWriteTime));
        }

        var streamHash = GetFileHash(path);
        var hdr = _file.LocateResource(streamHash);
        long fileSize;

        if (hdr == null)
        {
            if (Utilities.IsAllZeros(streamHash, 0, streamHash.Length))
            {
                fileSize = 0;
            }

            return new(this, path);
        }
        else
        {
            fileSize = hdr.OriginalSize;
        }

        return new CachedDiscFileInfo(this, path, dirEntry.Attributes, DateTime.FromFileTimeUtc(dirEntry.CreationTime),
                                            DateTime.FromFileTimeUtc(dirEntry.LastAccessTime),
                                            DateTime.FromFileTimeUtc(dirEntry.LastWriteTime), fileSize);
    }

    public override DiscFileInfo GetFileInfo(string path)
    {
        var dirEntry = GetEntry(path);
        if (dirEntry == null || dirEntry.Attributes.HasFlag(FileAttributes.Directory))
        {
            return new(this, path);
        }

        var streamHash = GetFileHash(path);
        var hdr = _file.LocateResource(streamHash);
        long fileSize;

        if (hdr == null)
        {
            if (Utilities.IsAllZeros(streamHash, 0, streamHash.Length))
            {
                fileSize = 0;
            }

            return new(this, path);
        }
        else
        {
            fileSize = hdr.OriginalSize;
        }

        return new CachedDiscFileInfo(this, path, dirEntry.Attributes, DateTime.FromFileTimeUtc(dirEntry.CreationTime),
                                            DateTime.FromFileTimeUtc(dirEntry.LastAccessTime),
                                            DateTime.FromFileTimeUtc(dirEntry.LastWriteTime), fileSize);
    }

    public override DiscDirectoryInfo GetDirectoryInfo(string path)
    {
        var dirEntry = GetEntry(path);
        if (dirEntry == null || !dirEntry.Attributes.HasFlag(FileAttributes.Directory))
        {
            return new(this, path);
        }

        return new CachedDiscDirectoryInfo(this, path, dirEntry.Attributes, DateTime.FromFileTimeUtc(dirEntry.CreationTime),
                                        DateTime.FromFileTimeUtc(dirEntry.LastAccessTime),
                                        DateTime.FromFileTimeUtc(dirEntry.LastWriteTime));
    }

    /// <summary>
    /// Gets the SHA-1 hash of a file's contents.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <returns>The 160-bit hash.</returns>
    /// <remarks>The WIM file format internally stores the SHA-1 hash of files.
    /// This method provides access to the stored hash.  Callers can use this
    /// value to compare against the actual hash of the byte stream to validate
    /// the integrity of the file contents.</remarks>
    public byte[] GetFileHash(string path)
    {
        SplitFileName(path, out var filePart, out var altStreamPart);

        var dirEntry = GetEntry(filePart);
        if (dirEntry == null)
        {
            throw new FileNotFoundException("No such file or directory", path);
        }

        return dirEntry.GetStreamHash(altStreamPart);
    }

    /// <summary>
    /// Size of the Filesystem in bytes
    /// </summary>
    public override long Size
    {
        get { throw new NotSupportedException("Filesystem size is not (yet) supported"); }
    }

    /// <summary>
    /// Used space of the Filesystem in bytes
    /// </summary>
    public override long UsedSpace
    {
        get { throw new NotSupportedException("Filesystem size is not (yet) supported"); }
    }

    /// <summary>
    /// Available space of the Filesystem in bytes
    /// </summary>
    public override long AvailableSpace
    {
        get { throw new NotSupportedException("Filesystem size is not (yet) supported"); }
    }

    public override bool SupportsUsedAvailableSpace => false;

    /// <summary>
    /// Disposes of this instance.
    /// </summary>
    /// <param name="disposing"><c>true</c> if disposing, else <c>false</c>.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_metaDataStream != null)
            {
                _metaDataStream.Dispose();
                _metaDataStream = null;
            }

            _file = null;
        }

        base.Dispose(disposing);
    }

    private static void SplitFileName(string path, out string filePart, out string altStreamPart)
    {
        var streamSepPos = path.IndexOf(":", StringComparison.Ordinal);

        if (streamSepPos >= 0)
        {
            filePart = path.Substring(0, streamSepPos);
            altStreamPart = path.Substring(streamSepPos + 1);
        }
        else
        {
            filePart = path;
            altStreamPart = string.Empty;
        }
    }

    private List<DirectoryEntry> GetDirectory(long id)
    {
        var dir = _dirCache[id];

        if (dir == null)
        {
            _metaDataStream.Position = id == 0 ? _rootDirPos : id;
            var reader = new LittleEndianDataReader(_metaDataStream);

            dir = new List<DirectoryEntry>();

            var entry = DirectoryEntry.ReadFrom(reader);

            while (entry != null)
            {
                dir.Add(entry);
                entry = DirectoryEntry.ReadFrom(reader);
            }

            _dirCache[id] = dir;
        }

        return dir;
    }

    private void ReadSecurityDescriptors()
    {
        var reader = new LittleEndianDataReader(_metaDataStream);

        var startPos = reader.Position;

        var totalLength = reader.ReadUInt32();
        var numEntries = reader.ReadUInt32();
        var sdLengths = new ulong[numEntries];

        for (uint i = 0; i < numEntries; ++i)
        {
            sdLengths[i] = reader.ReadUInt64();
        }

        _securityDescriptors = new List<RawSecurityDescriptor>((int)numEntries);
        for (uint i = 0; i < numEntries; ++i)
        {
            _securityDescriptors.Add(new RawSecurityDescriptor(reader.ReadBytes((int)sdLengths[i]), 0));
        }

        if (reader.Position < startPos + totalLength)
        {
            reader.Skip((int)(startPos + totalLength - reader.Position));
        }

        _rootDirPos = MathUtilities.RoundUp(startPos + totalLength, 8);
    }

    private DirectoryEntry GetEntry(string path)
    {
        if (path.EndsWithDirectorySeparator())
        {
            path = path.Substring(0, path.Length - 1);
        }

        if (!string.IsNullOrEmpty(path) && !path.StartsWithDirectorySeparator())
        {
            path = Path.DirectorySeparatorChar + path;
        }

        return GetEntry(GetDirectory(0), path.AsMemory().Split('/', '\\').ToArray());
    }

    private DirectoryEntry GetEntry(List<DirectoryEntry> dir, ReadOnlyMemory<char>[] path)
    {
        var currentDir = dir;
        DirectoryEntry nextEntry = null;

        for (var i = 0; i < path.Length; ++i)
        {
            nextEntry = null;

            foreach (var entry in currentDir)
            {
                if (path[i].Span.Equals(entry.FileName.AsSpan(), StringComparison.OrdinalIgnoreCase)
                    ||
                    (!string.IsNullOrEmpty(entry.ShortName) &&
                     path[i].Span.Equals(entry.ShortName.AsSpan(), StringComparison.OrdinalIgnoreCase)))
                {
                    nextEntry = entry;
                    break;
                }
            }

            if (nextEntry == null)
            {
                return null;
            }

            if (nextEntry.SubdirOffset != 0)
            {
                currentDir = GetDirectory(nextEntry.SubdirOffset);
            }
        }

        return nextEntry;
    }

    private IEnumerable<string> DoSearch(string path, Func<string, bool> filter, bool subFolders, bool dirs, bool files)
    {
        var parentDirEntry = GetEntry(path);

        if (parentDirEntry.SubdirOffset == 0)
        {
            yield break;
        }

        var parentDir = GetDirectory(parentDirEntry.SubdirOffset);

        foreach (var de in parentDir)
        {
            var isDir = (de.Attributes & FileAttributes.Directory) != 0;

            if ((isDir && dirs) || (!isDir && files))
            {
                if (filter is null || filter(de.SearchName))
                {
                    yield return Utilities.CombinePaths(path, de.FileName);
                }
            }

            if (subFolders && isDir)
            {
                foreach (var subdirentry in DoSearch(Utilities.CombinePaths(path, de.FileName), filter, subFolders, dirs, files))
                {
                    yield return subdirentry;
                }
            }
        }
    }
}
