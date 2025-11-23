// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DiscUtils.ExFat.Internal.Partition;

namespace DiscUtils.ExFat.Internal.Filesystem;
/// <inheritdoc />
/// <summary>
/// High-level file system, which works with paths
/// </summary>
/// <seealso cref="T:System.IDisposable" />
public class ExFatPathFilesystem : IDisposable
{
    [DebuggerDisplay("{" + nameof(DebugLiteral) + "}")]
    private readonly struct Path
    {
        private readonly string[] _allParts;

        public int Length { get; }

        public string Name => Length > 0 ? _allParts[Length - 1] : "";

        private string DebugLiteral => ToLiteral(System.IO.Path.DirectorySeparatorChar);

        public Path(string[] parts, int count)
        {
            _allParts = parts;
            Length = count;
        }

        public Path? GetParent()
        {
            if (Length == 0)
            {
                return null;
            }

            return new Path(_allParts, Length - 1);
        }

        public string ToLiteral(char separator) => string.Join(separator.ToString(), _allParts.Take(Length));
    }

    /// <summary>
    /// A node represents an entry.
    /// It has children, but not necessarily all of them.
    /// Nodes age and die (thus are invalid and need to be read again)
    /// </summary>
    private class Node
    {
        private readonly ExFatPathFilesystem _filesystem;
        private readonly Dictionary<string, Node> _children = [];
        private long _generation;
        private Node _parent;

        public ExFatFilesystemEntry Entry { get; }

        public Node(ExFatFilesystemEntry entry, ExFatPathFilesystem filesystem)
        {
            _filesystem = filesystem;
            _generation = _filesystem.GetNextGeneration();
            Entry = entry;
        }

        public Node GetChild(string childName)
        {
            lock (_children)
            {
                if (!_children.TryGetValue(childName, out var node))
                {
                    return null;
                }

                if (_filesystem.HasExpired(node._generation))
                {
                    _children.Remove(childName);
                    return null;
                }

                node._generation = _filesystem.GetNextGeneration();
                return node;
            }
        }

        public Node NewChild(Path path, ExFatFilesystemEntry entry)
        {
            var child = new Node(entry, _filesystem) { _parent = this };
            lock (_children)
            {
                _children[path.Name] = child;
            }

            return child;
        }

        public void RemoveChild(string childName)
        {
            lock (_children)
            {
                _children.Remove(childName);
            }
        }

        public void Remove()
        {
            if (_parent == null)
            {
                _children.Clear();
                return;
            }

            if (Entry != null)
            {
                _parent.RemoveChild(Entry.Name);
                return;
            }

            var keyValue = _parent._children.FirstOrDefault(kv => kv.Value == this);
            if (keyValue.Value == this)
            {
                _parent.RemoveChild(keyValue.Key);
            }
        }
    }

    private readonly ExFatEntryFilesystem _entryFilesystem;
    private readonly object _entriesLock = new();

    /// <summary>
    /// Gets or sets the path separators.
    /// </summary>
    /// <value>
    /// The path separators.
    /// </value>
    public char[] PathSeparators { get; set; }

    private readonly Node _rootNode;

    /// <summary>
    /// The default separators
    /// </summary>
    public static readonly char[] DefaultSeparators
        = [System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar];

    /// <summary>
    /// Gets the total size.
    /// </summary>
    /// <value>
    /// The total size.
    /// </value>
    public long TotalSpace => _entryFilesystem.TotalSpace;

    /// <summary>
    /// Gets the used space
    /// </summary>
    /// <value>
    /// The used space.
    /// </value>
    public long UsedSpace => _entryFilesystem.UsedSpace;

    /// <summary>
    /// Gets the available size.
    /// </summary>
    /// <value>
    /// The available size.
    /// </value>
    public long AvailableSpace => _entryFilesystem.AvailableSpace;

    /// <summary>
    /// Initializes a new instance of the <see cref="T:ExFat.Filesystem.ExFatPathFilesystem" /> class.
    /// </summary>
    /// <param name="partitionStream">The partition stream.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="pathSeparators">The path separators.</param>
    public ExFatPathFilesystem(Stream partitionStream, ExFatOptions flags = ExFatOptions.Default, char[] pathSeparators = null)
        : this(new ExFatPartition(partitionStream), flags, pathSeparators)
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="T:ExFat.Filesystem.ExFatPathFilesystem" /> class.
    /// </summary>
    /// <param name="partition">The partition.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="pathSeparators">The path separators.</param>
    /// <inheritdoc />
    public ExFatPathFilesystem(ExFatPartition partition, ExFatOptions flags = ExFatOptions.Default, char[] pathSeparators = null)
        : this(new ExFatEntryFilesystem(partition, flags), pathSeparators)
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExFatPathFilesystem" /> class.
    /// </summary>
    /// <param name="entryFilesystem">The entry filesystem.</param>
    /// <param name="pathSeparators">The path separators.</param>
    public ExFatPathFilesystem(ExFatEntryFilesystem entryFilesystem, char[] pathSeparators = null)
    {
        _entryFilesystem = entryFilesystem;
        PathSeparators = pathSeparators ?? DefaultSeparators;
        _rootNode = new Node(_entryFilesystem.RootDirectory, this);
    }

    private long _generation;
    private bool disposedValue;
    private readonly int _generationExpiry = Environment.ProcessorCount * (8 + 100);

    private long GetNextGeneration() => ++_generation;

    private bool HasExpired(long generation) => _generation - generation > _generationExpiry;

    /// <summary>
    /// Gets the node from the given path.
    /// Node itself is never null, but <see cref="Node.Entry"/> property may be
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    private Node GetNode(Path? path)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        if (path.Value.Length == 0)
        {
            return _rootNode;
        }

        lock (_entriesLock)
        {
            var parentNode = GetNode(path.Value.GetParent());
            var node = parentNode.GetChild(path.Value.Name);
            if (node != null)
            {
                return node;
            }

            return GetNode(parentNode, path.Value);
        }
    }

    private Node GetNode(Node parentNode, Path path)
    {
        var filesystemEntry = parentNode.Entry != null && parentNode.Entry.IsDirectory ? _entryFilesystem.FindChild(parentNode.Entry, path.Name) : null;
        return parentNode.NewChild(path, filesystemEntry);
    }

    private Path ParsePath(string literalPath)
    {
        var parts = literalPath.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
        return new Path(parts, parts.Length);
    }

    private string GetLiteralPath(Path parentPath, ExFatFilesystemEntry entry)
        => GetLiteralPath(parentPath.ToLiteral(PathSeparators[0]), entry);

    private string GetLiteralPath(string literalParentPath, ExFatFilesystemEntry entry)
    {
        var fileName = entry.MetaEntry.ExtensionsFileName;
        return GetLiteralPath(literalParentPath, fileName);
    }

    private string GetLiteralPath(string literalParentPath, string fileName)
    {
        // on root entries, the direct name is returned
        if (literalParentPath == "")
        {
            return fileName;
        }

        return $"{literalParentPath}{PathSeparators[0]}{fileName}";
    }

    private Node GetSafeNode(Path path)
    {
        var node = GetNode(path);
        if (node.Entry == null)
        {
            throw new FileNotFoundException();
        }

        return node;
    }

    private Node GetSafeDirectoryNode(Path directoryPath)
    {
        var directory = GetSafeNode(directoryPath);
        if (!directory.Entry.IsDirectory)
        {
            throw new IOException();
        }

        return directory;
    }

    private Node GetSafeNode(string literalPath) => GetSafeNode(ParsePath(literalPath));

    /// <summary>
    /// Performs action on entry and updates it to disk.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="update">The update.</param>
    private void UpdateSafeEntry(Path path, Action<ExFatFilesystemEntry> update)
    {
        lock (_entriesLock)
        {
            var entry = GetSafeNode(path);
            update(entry.Entry);
            _entryFilesystem.Update(entry.Entry);
        }
    }

    private void UpdateSafeEntry(string literalPath, Action<ExFatFilesystemEntry> update)
        => UpdateSafeEntry(ParsePath(literalPath), update);

    /// <summary>
    /// Enumerates the entries.
    /// </summary>
    /// <param name="literalDirectoryPath">The directory path.</param>
    /// <returns></returns>
    public IEnumerable<ExFatEntryInformation> EnumerateEntries(string literalDirectoryPath)
    {
        var directoryPath = ParsePath(literalDirectoryPath);
        var directoryNode = GetSafeDirectoryNode(directoryPath);
        lock (_entriesLock)
        {
            return _entryFilesystem.EnumerateFileSystemEntries(directoryNode.Entry)
                .Select(e => new ExFatEntryInformation(_entryFilesystem, e, GetLiteralPath(directoryPath, e))).ToArray();
        }
    }

    /// <summary>
    /// Gets the creation time.
    /// </summary>
    /// <param name="literalPath">The path.</param>
    /// <returns></returns>
    public DateTime GetCreationTime(string literalPath) => GetSafeNode(literalPath).Entry.CreationTime;
    /// <summary>
    /// Gets the creation time UTC.
    /// </summary>
    /// <param name="literalPath">The path.</param>
    /// <returns></returns>
    public DateTime GetCreationTimeUtc(string literalPath) => GetSafeNode(literalPath).Entry.CreationTimeUtc;
    /// <summary>
    /// Gets the last write time.
    /// </summary>
    /// <param name="literalPath">The path.</param>
    /// <returns></returns>
    public DateTime GetLastWriteTime(string literalPath) => GetSafeNode(literalPath).Entry.LastWriteTime;
    /// <summary>
    /// Gets the last write time UTC.
    /// </summary>
    /// <param name="literalPath">The path.</param>
    /// <returns></returns>
    public DateTime GetLastWriteTimeUtc(string literalPath) => GetSafeNode(literalPath).Entry.LastWriteTimeUtc;
    /// <summary>
    /// Gets the last access time.
    /// </summary>
    /// <param name="literalPath">The path.</param>
    /// <returns></returns>
    public DateTime GetLastAccessTime(string literalPath) => GetSafeNode(literalPath).Entry.LastAccessTime;
    /// <summary>
    /// Gets the last access time UTC.
    /// </summary>
    /// <param name="literalPath">The path.</param>
    /// <returns></returns>
    public DateTime GetLastAccessTimeUtc(string literalPath) => GetSafeNode(literalPath).Entry.LastAccessTimeUtc;

    /// <summary>
    /// Sets the creation time.
    /// </summary>
    /// <param name="literalPath">The path.</param>
    /// <param name="creationTime">The creation time.</param>
    public void SetCreationTime(string literalPath, DateTime creationTime) => UpdateSafeEntry(literalPath, e => e.CreationDateTimeOffset = creationTime.ToLocalTime());
    /// <summary>
    /// Sets the creation time UTC.
    /// </summary>
    /// <param name="literalPath">The path.</param>
    /// <param name="creationTime">The creation time.</param>
    public void SetCreationTimeUtc(string literalPath, DateTime creationTime) => UpdateSafeEntry(literalPath, e => e.CreationDateTimeOffset = creationTime.ToUniversalTime());
    /// <summary>
    /// Sets the last write time.
    /// </summary>
    /// <param name="literalPath">The path.</param>
    /// <param name="lastWriteTime">The last write time.</param>
    public void SetLastWriteTime(string literalPath, DateTime lastWriteTime) => UpdateSafeEntry(literalPath, e => e.LastWriteDateTimeOffset = lastWriteTime.ToLocalTime());
    /// <summary>
    /// Sets the last write time UTC.
    /// </summary>
    /// <param name="literalPath">The path.</param>
    /// <param name="lastWriteTime">The last write time.</param>
    public void SetLastWriteTimeUtc(string literalPath, DateTime lastWriteTime) => UpdateSafeEntry(literalPath, e => e.LastWriteDateTimeOffset = lastWriteTime.ToUniversalTime());
    /// <summary>
    /// Sets the last access time.
    /// </summary>
    /// <param name="literalPath">The path.</param>
    /// <param name="lastAccessTime">The last access time.</param>
    public void SetLastAccessTime(string literalPath, DateTime lastAccessTime) => UpdateSafeEntry(literalPath, e => e.LastAccessDateTimeOffset = lastAccessTime.ToLocalTime());
    /// <summary>
    /// Sets the last access time UTC.
    /// </summary>
    /// <param name="literalPath">The path.</param>
    /// <param name="lastAccessTime">The last access time.</param>
    public void SetLastAccessTimeUtc(string literalPath, DateTime lastAccessTime) => UpdateSafeEntry(literalPath, e => e.LastAccessDateTimeOffset = lastAccessTime.ToUniversalTime());

    private Node CreateDirectoryNode(Path? path)
    {
        lock (_entriesLock)
        {
            var existingDirectory = GetNode(path);
            if (existingDirectory.Entry != null)
            {
                return existingDirectory;
            }

            var parentDirectory = CreateDirectoryNode(path.Value.GetParent());
            var directoryEntry = _entryFilesystem.CreateDirectory(parentDirectory.Entry, path.Value.Name);
            return parentDirectory.NewChild(path.Value, directoryEntry);
        }
    }

    /// <summary>
    /// Creates a directory under the given path.
    /// </summary>
    /// <param name="literalPath">The path.</param>
    public void CreateDirectory(string literalPath) => CreateDirectoryNode(ParsePath(literalPath));

    /// <summary>
    /// Deletes the specified entry at given path.
    /// </summary>
    /// <param name="literalPath">The path.</param>
    /// <exception cref="IOException"></exception>
    public void Delete(string literalPath)
    {
        lock (_entriesLock)
        {
            var node = GetSafeNode(literalPath);
            if (node.Entry.IsDirectory)
            {
                if (_entryFilesystem.EnumerateFileSystemEntries(node.Entry).Any())
                {
                    throw new IOException();
                }
            }

            _entryFilesystem.Delete(node.Entry);
            node.Remove();
        }
    }

    /// <summary>
    /// Deletes the tree at given path.
    /// If the entry is a file, simply deletes the file.
    /// </summary>
    /// <param name="literalPath">The path.</param>
    public void DeleteTree(string literalPath)
    {
        lock (_entriesLock)
        {
            var cleanPath = ParsePath(literalPath);
            var entry = GetSafeNode(cleanPath);
            if (entry.Entry.IsDirectory)
            {
                foreach (var childPath in EnumerateEntries(cleanPath.ToLiteral(PathSeparators[0])))
                {
                    DeleteTree(childPath.Path);
                }
            }

            Delete(literalPath);
        }
    }

    /// <summary>
    /// Gets the information.
    /// </summary>
    /// <param name="literalPath">The path.</param>
    /// <returns></returns>
    public ExFatEntryInformation GetInformation(string literalPath)
    {
        lock (_entriesLock)
        {
            var cleanPath = ParsePath(literalPath);
            var node = GetNode(cleanPath);
            if (node.Entry == null)
            {
                return null;
            }

            return new ExFatEntryInformation(_entryFilesystem, node.Entry, cleanPath.ToLiteral(PathSeparators[0]));
        }
    }

    /// <summary>
    /// Opens or creates a file.
    /// </summary>
    /// <param name="literalPath">The path.</param>
    /// <param name="mode">The mode.</param>
    /// <param name="access">The access.</param>
    /// <returns></returns>
    /// <exception cref="DirectoryNotFoundException"></exception>
    /// <exception cref="FileNotFoundException"></exception>
    /// <exception cref="IOException">
    /// </exception>
    public Stream Open(string literalPath, FileMode mode, FileAccess access)
    {
        lock (_entriesLock)
        {
            var path = ParsePath(literalPath);
            var parentEntry = GetNode(path.GetParent());
            if (parentEntry == null || !parentEntry.Entry.IsDirectory)
            {
                throw new DirectoryNotFoundException();
            }

            var child = _entryFilesystem.FindChild(parentEntry.Entry, path.Name);
            // not existing?
            if (child == null)
            {
                if (mode is FileMode.Append or FileMode.Open or FileMode.Truncate)
                {
                    throw new FileNotFoundException();
                }

                parentEntry.RemoveChild(path.Name);
                return _entryFilesystem.CreateFile(parentEntry.Entry, path.Name);
            }

            if (child.IsDirectory)
            {
                throw new IOException();
            }

            if (mode == FileMode.CreateNew)
            {
                throw new IOException();
            }

            var stream = _entryFilesystem.OpenFile(child, access);
            if (mode == FileMode.Truncate)
            {
                stream.SetLength(0);
            }

            if (mode == FileMode.Append)
            {
                stream.Seek(0, SeekOrigin.End);
            }

            return stream;
        }
    }

    /// <summary>
    /// Moves the specified source.
    /// </summary>
    /// <param name="sourceLiteralPath">The source path.</param>
    /// <param name="targetDirectoryLiteralPath">The target directory. null to stay in same directory</param>
    /// <param name="targetName">Name of the target. null to keep original name</param>
    public void Move(string sourceLiteralPath, string targetDirectoryLiteralPath, string targetName = null)
    {
        lock (_entriesLock)
        {
            if (targetDirectoryLiteralPath == null && targetName == null)
            {
                throw new ArgumentNullException(nameof(targetDirectoryLiteralPath), "Either targetDirectory or targetName has to be provided");
            }

            var sourcePath = ParsePath(sourceLiteralPath);
            var targetDirectory = targetDirectoryLiteralPath == null ? sourcePath.GetParent() : ParsePath(targetDirectoryLiteralPath);

            var sourceEntry = GetNode(sourcePath) ?? throw new FileNotFoundException();
            var targetDirectoryEntry = GetNode(targetDirectory) ?? throw new FileNotFoundException();
            _entryFilesystem.Move(sourceEntry.Entry, targetDirectoryEntry.Entry, targetName);
            sourceEntry.Remove();
            targetDirectoryEntry.Remove();
        }
    }

    /// <summary>
    /// Formats the specified partition stream.
    /// </summary>
    /// <param name="partitionStream">The partition stream.</param>
    /// <param name="options">The options.</param>
    /// <param name="volumeLabel">The volume label.</param>
    /// <returns></returns>
    public static ExFatPathFilesystem Format(Stream partitionStream, ExFatFormatOptions options, string volumeLabel = null)
    {
        var entryFilesystem = ExFatEntryFilesystem.Format(partitionStream, options, volumeLabel);
        return new ExFatPathFilesystem(entryFilesystem);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
                _entryFilesystem.Dispose();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer

            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    ~ExFatPathFilesystem()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
