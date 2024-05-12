using System;
using DiscUtils.Core.WindowsSecurity.AccessControl;
using System.IO;
using System.Linq;
using DiscUtils.Internal;
using DiscUtils.Streams;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DiscUtils.VirtualFileSystem;
public partial class VirtualFileSystem : DiscFileSystem, IWindowsFileSystem, IUnixFileSystem, IFileSystemBuilder
{
    public delegate Stream FileOpenDelegate(FileMode mode, FileAccess access);

    // Progress reporting event
    public event EventHandler<ProgressEventArgs> ProgressChanged;

    private ProgressEventArgs progressEventArgs;

    // Method for updating progress
    internal void AddProgress(int newFiles, int newItems)
    {
        if (ProgressChanged is not null)
        {
            progressEventArgs ??= new();
            progressEventArgs.TotalFiles += newFiles;
            progressEventArgs.TotalItems += newItems;
            ProgressChanged(this, progressEventArgs);
        }
    }

    public static string GetPathDirectoryName(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var index = path.LastIndexOfAny(Utilities.PathSeparators);

        if (index >= 0)
        {
            return path.Remove(index);
        }

        return string.Empty;
    }

    public static string GetPathFileName(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var index = path.LastIndexOfAny(Utilities.PathSeparators);

        if (index >= 0)
        {
            return path.Substring(index + 1);
        }

        return path;
    }

    private readonly VirtualFileSystemDirectory _root;

    private long _used_space;

    public VirtualFileSystem(VirtualFileSystemOptions options)
        : base(options)
    {
        _root = new VirtualFileSystemDirectory(this);
    }

    public event EventHandler<CreateFileEventArgs> CreateFile;

    public new VirtualFileSystemOptions Options => (VirtualFileSystemOptions)base.Options;

    public override bool CanWrite => Options.CanWrite;

    public override bool IsThreadSafe => Options.IsThreadSafe;

    public override string VolumeLabel => Options.VolumeLabel;

    public override string FriendlyName => "VirtualFileSystem";

    public override long Size => _used_space;

    public override long UsedSpace => _used_space;

    public override long AvailableSpace => 0;

    int IFileSystemBuilder.FileCount => _root.EnumerateTreeEntries().Count();

    long IFileSystemBuilder.TotalSize => _used_space;

    public string VolumeIdentifier
    {
        get => Options.VolumeLabel;
        set => Options.VolumeLabel = value;
    }
    public IEqualityComparer<string> NameComparer =>
        Options.CaseSensitive
        ? StringComparer.Ordinal
        : StringComparer.OrdinalIgnoreCase;

    string IFileSystemBuilder.VolumeIdentifier { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public virtual void SetUsedSpace(long size) => _used_space = size;

    public virtual long UpdateUsedSpace() =>
        _used_space = _root.EnumerateTreeEntries()
        .OfType<VirtualFileSystemFile>()
        .Sum(file => file.AllocationLength);

    public override void CopyFile(string sourceFile, string destinationFile, bool overwrite) =>
        throw new NotImplementedException();

    public virtual VirtualFileSystemDirectoryEntry AddLink(string existing, string new_path)
    {
        if (!CanWrite)
        {
            throw new IOException("Volume is not writable");
        }

        var entry = _root.ResolvePathToEntry(existing)
            ?? throw new FileNotFoundException("File not found", existing);

        var destination = _root.CreateSubDirectoryTree(GetPathDirectoryName(new_path));

        return entry.AddLink(destination, GetPathFileName(new_path));
    }

    public virtual VirtualFileSystemDirectoryEntry AddLink(VirtualFileSystemDirectoryEntry entry, string new_path)
    {
        if (!CanWrite)
        {
            throw new IOException("Volume is not writable");
        }

        var destination = _root.CreateSubDirectoryTree(GetPathDirectoryName(new_path));

        return entry.AddLink(destination, GetPathFileName(new_path));
    }

    public sealed override void CreateDirectory(string path) => AddDirectory(path);

    public virtual VirtualFileSystemDirectory AddDirectory(string path)
    {
        if (!CanWrite)
        {
            throw new IOException("Volume is not writable");
        }

        return _root.CreateSubDirectoryTree(path);
    }

    public override void DeleteDirectory(string path, bool recursive)
    {
        if (!CanWrite)
        {
            throw new IOException("Volume is not writable");
        }

        if (_root.ResolvePathToEntry(path) is not VirtualFileSystemDirectory directory)
        {
            throw new DirectoryNotFoundException();
        }

        directory.Delete(recursive);
    }

    public override void DeleteDirectory(string path) => DeleteDirectory(path, recursive: false);

    public override void DeleteFile(string path)
    {
        if (!CanWrite)
        {
            throw new IOException("Volume is not writable");
        }

        if (_root.ResolvePathToEntry(path) is not VirtualFileSystemFile file)
        {
            throw new FileNotFoundException("File not found", path);
        }

        file.Delete();
    }

    public override bool DirectoryExists(string path) => _root.ResolvePathToEntry(path) is VirtualFileSystemDirectory;

    public override bool FileExists(string path) => _root.ResolvePathToEntry(path) is VirtualFileSystemFile;

    public override bool Exists(string path) => _root.ResolvePathToEntry(path) != null;

    public override IEnumerable<string> GetDirectories(string path, string searchPattern, SearchOption searchOption)
    {
        var directory = _root.ResolvePathToEntry(path) as VirtualFileSystemDirectory ??
            throw new DirectoryNotFoundException();

        var filter = GetFilter(searchPattern);

        if (searchOption == SearchOption.TopDirectoryOnly)
        {
            return directory.EnumerateDirectories()
                .Select(entry => entry.Key)
                .Where(filter)
                .Select(name => Path.Combine(path, name));
        }

        return directory.EnumerateTree()
            .Where(entry => entry.Value is VirtualFileSystemDirectory)
            .Select(entry => entry.Key)
            .Where(name => filter(GetPathFileName(name)))
            .Select(name => Path.Combine(path, name));
    }

    public Func<string, bool> GetFilter(string pattern)
    {
        if (string.IsNullOrEmpty(pattern) ||
            pattern == "*" || pattern == "*.*")
        {
            return name => true;
        }
        else if (pattern.AsSpan().IndexOfAny('?', '*') < 0)
        {
            return name => NameComparer.Equals(pattern, name);
        }
        else
        {
            var query = $"^{Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", "[^.]")}$";
            var regexOptions = RegexOptions.CultureInvariant;
            if (!Options.CaseSensitive)
            {
                regexOptions |= RegexOptions.IgnoreCase;
            }

            return new Regex(query, regexOptions).IsMatch;
        }
    }

    public override IEnumerable<string> GetFiles(string path, string searchPattern, SearchOption searchOption)
    {
        var directory = _root.ResolvePathToEntry(path) as VirtualFileSystemDirectory ??
            throw new DirectoryNotFoundException();

        var filter = GetFilter(searchPattern);

        if (searchOption == SearchOption.TopDirectoryOnly)
        {
            return directory.EnumerateFiles()
                .Select(entry => entry.Key)
                .Where(filter)
                .Select(name => Path.Combine(path, name));
        }

        return directory.EnumerateTree()
            .Where(entry => entry.Value is VirtualFileSystemFile)
            .Select(entry => entry.Key)
            .Where(name => filter(GetPathFileName(name)))
            .Select(name => Path.Combine(path, name));
    }

    public override IEnumerable<string> GetFileSystemEntries(string path)
    {
        var directory = _root.ResolvePathToEntry(path) as VirtualFileSystemDirectory ??
            throw new DirectoryNotFoundException();

        return directory.GetNames()
            .Select(name => Path.Combine(path, name));
    }

    public override IEnumerable<string> GetFileSystemEntries(string path, string searchPattern)
    {
        var directory = _root.ResolvePathToEntry(path) as VirtualFileSystemDirectory ??
            throw new DirectoryNotFoundException();

        return directory.GetNames()
            .Where(GetFilter(searchPattern))
            .Select(name => Path.Combine(path, name));
    }

    public override void MoveDirectory(string sourceDirectoryName, string destinationDirectoryName)
    {
        var directory = _root.ResolvePathToEntry(sourceDirectoryName) as VirtualFileSystemDirectory ??
            throw new DirectoryNotFoundException();

        var destination = AddDirectory(GetPathDirectoryName(destinationDirectoryName));

        directory.Move(destination, GetPathFileName(destinationDirectoryName), replace: false);
    }

    public override void MoveFile(string sourceName, string destinationName, bool overwrite)
    {
        var file = _root.ResolvePathToEntry(sourceName) as VirtualFileSystemFile ??
            throw new FileNotFoundException("File not found", sourceName);

        var destination = AddDirectory(GetPathDirectoryName(destinationName));

        file.Move(destination, GetPathFileName(destinationName), overwrite);
    }

    public VirtualFileSystemFile AddFile(string path, byte[] data) =>
        new(AddDirectory(GetPathDirectoryName(path)),
            GetPathFileName(path),
            (mode, access) => new MemoryStream(data, access.HasFlag(FileAccess.Write)))
        {
            Length = data.Length
        };

    public VirtualFileSystemFile AddFile(string path, byte[] data, int index, int count) =>
        new(AddDirectory(GetPathDirectoryName(path)),
            GetPathFileName(path),
            (mode, access) => new MemoryStream(data, index, count, access.HasFlag(FileAccess.Write)))
        {
            Length = count
        };

    public VirtualFileSystemFile AddFile(string path, FileOpenDelegate open) =>
        new(AddDirectory(GetPathDirectoryName(path)),
            GetPathFileName(path),
            open);

    public VirtualFileSystemFile AddFile(string path, DiscFileInfo existingFile) =>
        new(AddDirectory(GetPathDirectoryName(path)),
            GetPathFileName(path),
            existingFile.Open)
        {
            Attributes = existingFile.Attributes,
            CreationTimeUtc = existingFile.CreationTimeUtc,
            LastAccessTimeUtc = existingFile.LastAccessTimeUtc,
            LastWriteTimeUtc = existingFile.LastWriteTimeUtc,
            Length = existingFile.Length
        };

    public VirtualFileSystemFile AddFile(string path, IFileSystem fileSystem, string existingPath) =>
        new(AddDirectory(GetPathDirectoryName(path)),
            GetPathFileName(path),
            (mode, access) => fileSystem.OpenFile(existingPath, mode, access))
        {
            Attributes = fileSystem.GetAttributes(existingPath),
            CreationTimeUtc = fileSystem.GetCreationTimeUtc(existingPath),
            LastAccessTimeUtc = fileSystem.GetLastAccessTimeUtc(existingPath),
            LastWriteTimeUtc = fileSystem.GetLastWriteTimeUtc(existingPath),
            Length = fileSystem.GetFileLength(existingPath)
        };

    public VirtualFileSystemFile AddFile(string path, string existingPhysicalPath) =>
        new(AddDirectory(GetPathDirectoryName(path)),
            GetPathFileName(path),
            (mode, access) => File.Open(existingPhysicalPath, mode, access))
        {
            Attributes = File.GetAttributes(existingPhysicalPath),
            CreationTimeUtc = File.GetCreationTimeUtc(existingPhysicalPath),
            LastAccessTimeUtc = File.GetLastAccessTimeUtc(existingPhysicalPath),
            LastWriteTimeUtc = File.GetLastWriteTimeUtc(existingPhysicalPath),
            Length = new FileInfo(existingPhysicalPath).Length
        };

    public VirtualFileSystemDirectory AddDirectory(string name, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes)
    {
        var member = AddDirectory(name);
        member.Attributes = attributes;
        member.CreationTimeUtc = creationTime.ToUniversalTime();
        member.LastWriteTimeUtc = writtenTime.ToUniversalTime();
        member.LastAccessTimeUtc = accessedTime.ToUniversalTime();
        return member;
    }

    public VirtualFileSystemFile AddFile(string path, byte[] content, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes) =>
        new(AddDirectory(GetPathDirectoryName(path)),
            GetPathFileName(path),
            (mode, access) => new MemoryStream(content, access.HasFlag(FileAccess.Write)))
        {
            Attributes = attributes,
            CreationTimeUtc = creationTime.ToUniversalTime(),
            LastAccessTimeUtc = accessedTime.ToUniversalTime(),
            LastWriteTimeUtc = writtenTime.ToUniversalTime(),
            Length = content.Length
        };

    public VirtualFileSystemFile AddFile(string path, Stream source, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes) =>
        new(AddDirectory(GetPathDirectoryName(path)),
            GetPathFileName(path), (mode, access) => SparseStream.FromStream(source, Ownership.None))
        {
            Attributes = attributes,
            CreationTimeUtc = creationTime.ToUniversalTime(),
            LastAccessTimeUtc = accessedTime.ToUniversalTime(),
            LastWriteTimeUtc = writtenTime.ToUniversalTime(),
            Length = source.Length
        };

    public VirtualFileSystemFile AddFile(string path, string existingPhysicalPath, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes) =>
        new(AddDirectory(GetPathDirectoryName(path)),
            GetPathFileName(path),
            (mode, access) => File.Open(existingPhysicalPath, mode, access))
        {
            Attributes = attributes,
            CreationTimeUtc = creationTime.ToUniversalTime(),
            LastAccessTimeUtc = accessedTime.ToUniversalTime(),
            LastWriteTimeUtc = writtenTime.ToUniversalTime(),
            Length = new FileInfo(existingPhysicalPath).Length
        };

    public override SparseStream OpenFile(string path, FileMode mode, FileAccess access)
    {
        if (_root.ResolvePathToEntry(path) is not VirtualFileSystemFile file)
        {
            if (mode == FileMode.Open)
            {
                throw new FileNotFoundException("File not found", path);
            }

            var e = new CreateFileEventArgs
            {
                Path = path,
                Mode = mode,
                Access = access
            };

            OnCreateFile(e);

            file = e.Result;

            if (file == null)
            {
                throw new NotImplementedException($"Could not create file '{path}'.");
            }
        }

        return file.Open(mode, access);
    }

    protected virtual void OnCreateFile(CreateFileEventArgs e) => CreateFile?.Invoke(this, e);

    public override FileAttributes GetAttributes(string path)
    {
        var file = _root.ResolvePathToEntry(path) ??
            throw new FileNotFoundException("File not found", path);

        return file.Attributes;
    }

    public override void SetAttributes(string path, FileAttributes newValue)
    {
        var file = _root.ResolvePathToEntry(path) ??
            throw new FileNotFoundException("File not found", path);

        file.Attributes = newValue;
    }

    public override DateTime GetCreationTimeUtc(string path)
    {
        var file = _root.ResolvePathToEntry(path) ??
            throw new FileNotFoundException("File not found", path);

        return file.CreationTimeUtc;
    }

    public override void SetCreationTimeUtc(string path, DateTime newTime)
    {
        var file = _root.ResolvePathToEntry(path) ??
            throw new FileNotFoundException("File not found", path);

        file.CreationTimeUtc = newTime;
    }

    public override DateTime GetLastAccessTimeUtc(string path)
    {
        var file = _root.ResolvePathToEntry(path) ??
            throw new FileNotFoundException("File not found", path);

        return file.LastAccessTimeUtc;
    }

    public override void SetLastAccessTimeUtc(string path, DateTime newTime)
    {
        var file = _root.ResolvePathToEntry(path) ??
            throw new FileNotFoundException("File not found", path);

        file.LastAccessTimeUtc = newTime;
    }

    public override DateTime GetLastWriteTimeUtc(string path)
    {
        var file = _root.ResolvePathToEntry(path) ??
            throw new FileNotFoundException("File not found", path);

        return file.LastWriteTimeUtc;
    }

    public override void SetLastWriteTimeUtc(string path, DateTime newTime)
    {
        var file = _root.ResolvePathToEntry(path) ??
            throw new FileNotFoundException("File not found", path);

        file.LastWriteTimeUtc = newTime;
    }

    public override long GetFileLength(string path)
    {
        var file = _root.ResolvePathToEntry(path) as VirtualFileSystemFile ??
            throw new FileNotFoundException("File not found", path);

        return file.Length;
    }

    public override DiscFileInfo GetFileInfo(string path)
    {
        if (_root.ResolvePathToEntry(path) is not VirtualFileSystemFile file)
        {
            return new(this, path);
        }

        return new CachedDiscFileInfo(this, path, file.Attributes, file.CreationTimeUtc, file.LastAccessTimeUtc, file.LastWriteTimeUtc, file.Length);
    }

    public override DiscDirectoryInfo GetDirectoryInfo(string path)
    {
        if (_root.ResolvePathToEntry(path) is not VirtualFileSystemDirectory dir)
        {
            return new(this, path);
        }

        return new CachedDiscDirectoryInfo(this, path, dir.Attributes, dir.CreationTimeUtc, dir.LastAccessTimeUtc, dir.LastWriteTimeUtc);
    }

    public override DiscFileSystemInfo GetFileSystemInfo(string path)
    {
        var dirEntry = _root.ResolvePathToEntry(path);

        if (dirEntry == null)
        {
            return new(this, path);
        }

        if (dirEntry is VirtualFileSystemFile file)
        {
            return new CachedDiscFileInfo(this, path, file.Attributes, file.CreationTimeUtc, file.LastAccessTimeUtc, file.LastWriteTimeUtc, file.Length);
        }
        else
        {
            return new CachedDiscDirectoryInfo(this, path, dirEntry.Attributes, dirEntry.CreationTimeUtc, dirEntry.LastAccessTimeUtc, dirEntry.LastWriteTimeUtc);
        }
    }

    public UnixFileSystemInfo GetUnixFileInfo(string path)
    {
        var dirEntry = _root.ResolvePathToEntry(path)
            ?? throw new FileNotFoundException("File not found", path);

        var isDirectory = dirEntry is VirtualFileSystemDirectory;

        var ufsinfo = new UnixFileSystemInfo
        {
            FileType = isDirectory ? UnixFileType.Directory : UnixFileType.Regular,
            UserId = dirEntry.UnixOwnerId,
            GroupId = dirEntry.UnixGroupId,
            Permissions = dirEntry.UnixFileMode,
            Inode = dirEntry.FileId
        };

        if (ufsinfo.Permissions == VirtualFileSystemDirectoryEntry.DefaultUnixFilePermissions
            && dirEntry.Attributes.HasFlag(FileAttributes.ReadOnly))
        {
            ufsinfo.Permissions &= ~(UnixFilePermissions.OwnerWrite | UnixFilePermissions.GroupWrite | UnixFilePermissions.OthersWrite);
        }

        return ufsinfo;
    }

    public void Freeze()
    {
        Options.CanWrite = false;
        UpdateUsedSpace();
    }

    public override string ToString() => VolumeLabel ?? FriendlyName ?? base.ToString();

    void IFileSystemBuilder.AddDirectory(string name) =>
        AddDirectory(name);

    public VirtualFileSystemDirectory AddDirectory(string name,
                                                   int ownerId,
                                                   int groupId,
                                                   UnixFilePermissions fileMode,
                                                   DateTime creationTime,
                                                   DateTime modificationTime,
                                                   DateTime accessedTime)
    {
        var member = AddDirectory(name);

        member.CreationTimeUtc = creationTime.ToUniversalTime();
        member.LastWriteTimeUtc = modificationTime.ToUniversalTime();
        member.LastAccessTimeUtc = accessedTime.ToUniversalTime();

        member.Attributes = Utilities.FileAttributesFromUnixFilePermissions(name, fileMode, UnixFileType.Directory);

        member.UnixOwnerId = ownerId;
        member.UnixGroupId = groupId;
        member.UnixFileMode = fileMode;

        return member;
    }

    void IFileSystemBuilder.AddFile(string name, byte[] content) =>
        AddFile(name, content);

    public VirtualFileSystemFile AddFile(string name,
                                         byte[] content,
                                         int ownerId,
                                         int groupId,
                                         UnixFilePermissions fileMode,
                                         UnixFileType fileType,
                                         DateTime creationTime,
                                         DateTime modificationTime,
                                         DateTime accessedTime)
    {
        var member = AddFile(name, content);

        member.CreationTimeUtc = creationTime.ToUniversalTime();
        member.LastWriteTimeUtc = modificationTime.ToUniversalTime();
        member.LastAccessTimeUtc = accessedTime.ToUniversalTime();

        member.Attributes = Utilities.FileAttributesFromUnixFilePermissions(name, fileMode, fileType);

        member.UnixOwnerId = ownerId;
        member.UnixGroupId = groupId;
        member.UnixFileMode = fileMode;

        return member;
    }

    void IFileSystemBuilder.AddFile(string name, Stream source)
    {
        var member = AddFile(name, (mode, access) => SparseStream.FromStream(source, Ownership.None));

        member.Length = source.Length;

        if (!source.CanWrite)
        {
            member.Attributes |= FileAttributes.ReadOnly;
        }
    }

    public VirtualFileSystemFile AddFile(string name,
                                         Stream source,
                                         int ownerId,
                                         int groupId,
                                         UnixFilePermissions fileMode,
                                         UnixFileType fileType,
                                         DateTime creationTime,
                                         DateTime modificationTime,
                                         DateTime accessedTime)
    {
        var member = AddFile(name, (mode, access) => SparseStream.FromStream(source, Ownership.None));

        member.Length = source.Length;

        member.CreationTimeUtc = creationTime.ToUniversalTime();
        member.LastWriteTimeUtc = modificationTime.ToUniversalTime();
        member.LastAccessTimeUtc = accessedTime.ToUniversalTime();

        member.Attributes = Utilities.FileAttributesFromUnixFilePermissions(name, fileMode, fileType);

        if (!source.CanWrite)
        {
            member.Attributes |= FileAttributes.ReadOnly;
        }

        member.UnixOwnerId = ownerId;
        member.UnixGroupId = groupId;
        member.UnixFileMode = fileMode;

        return member;
    }

    void IFileSystemBuilder.AddFile(string name, string sourcePath) =>
        AddFile(name, sourcePath);

    public VirtualFileSystemFile AddFile(string name,
                                         string sourcePath,
                                         int ownerId,
                                         int groupId,
                                         UnixFilePermissions fileMode,
                                         UnixFileType fileType,
                                         DateTime creationTime,
                                         DateTime modificationTime,
                                         DateTime accessedTime)
    {
        var member = AddFile(name, sourcePath);

        member.CreationTimeUtc = creationTime.ToUniversalTime();
        member.LastWriteTimeUtc = modificationTime.ToUniversalTime();
        member.LastAccessTimeUtc = accessedTime.ToUniversalTime();

        member.Attributes = Utilities.FileAttributesFromUnixFilePermissions(name, fileMode, fileType);

        if (File.GetAttributes(sourcePath).HasFlag(FileAttributes.ReadOnly))
        {
            member.Attributes |= FileAttributes.ReadOnly;
        }

        member.UnixOwnerId = ownerId;
        member.UnixGroupId = groupId;
        member.UnixFileMode = fileMode;

        return member;
    }

    IFileSystem IFileSystemBuilder.GenerateFileSystem() => this;

    public virtual RawSecurityDescriptor GetSecurity(string path)
    {
        var file = _root.ResolvePathToEntry(path) ??
            throw new FileNotFoundException("File not found", path);

        return file.SecurityDescriptor;
    }

    public virtual void SetSecurity(string path, RawSecurityDescriptor securityDescriptor)
    {
        var file = _root.ResolvePathToEntry(path) ??
            throw new FileNotFoundException("File not found", path);

        file.SecurityDescriptor = securityDescriptor;
    }

    public virtual ReparsePoint GetReparsePoint(string path)
    {
        var file = _root.ResolvePathToEntry(path) ??
            throw new FileNotFoundException("File not found", path);

        return file.ReparsePoint;
    }

    public virtual void SetReparsePoint(string path, ReparsePoint reparsePoint)
    {
        var file = _root.ResolvePathToEntry(path) ??
            throw new FileNotFoundException("File not found", path);

        file.ReparsePoint = reparsePoint;

        file.Attributes |= FileAttributes.ReparsePoint;
    }

    public virtual void RemoveReparsePoint(string path)
    {
        var file = _root.ResolvePathToEntry(path) ??
            throw new FileNotFoundException("File not found", path);

        file.ReparsePoint = null;

        file.Attributes &= ~FileAttributes.ReparsePoint;
    }

    public virtual WindowsFileInformation GetFileStandardInformation(string path)
    {
        var file = _root.ResolvePathToEntry(path) ??
            throw new FileNotFoundException("File not found", path);

        return file.GetStandardInformation();
    }

    public virtual void SetFileStandardInformation(string path, WindowsFileInformation info)
    {
        var file = _root.ResolvePathToEntry(path) ??
            throw new FileNotFoundException("File not found", path);

        file.SetStandardInformation(info);
    }

    public virtual IEnumerable<string> GetAlternateDataStreams(string path) => Enumerable.Empty<string>();

    public virtual bool HasHardLinks(string path) => false;

    public virtual int GetHardLinkCount(string path) => 1;

    public virtual string GetShortName(string path)
    {
        var file = _root.ResolvePathToEntry(path) ??
            throw new FileNotFoundException("File not found", path);

        return file.ShortName;
    }

    public virtual void SetShortName(string path, string shortName)
    {
        var file = _root.ResolvePathToEntry(path) ??
            throw new FileNotFoundException("File not found", path);

        file.ShortName = shortName;
    }

    public virtual long GetFileId(string path)
    {
        var file = _root.ResolvePathToEntry(path) ??
            throw new FileNotFoundException("File not found", path);

        return file.FileId;
    }

    void IFileSystemBuilder.AddDirectory(string name, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes)
        => AddDirectory(name, creationTime, writtenTime, accessedTime, attributes);

    void IFileSystemBuilder.AddFile(string name, byte[] content, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes)
        => AddFile(name, content, creationTime, writtenTime, accessedTime, attributes);

    void IFileSystemBuilder.AddFile(string name, Stream source, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes)
        => AddFile(name, source, creationTime, writtenTime, accessedTime, attributes);

    void IFileSystemBuilder.AddFile(string name, string sourcePath, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes)
        => AddFile(name, sourcePath, creationTime, writtenTime, accessedTime, attributes);

    void IFileSystemBuilder.AddDirectory(string name, int ownerId, int groupId, UnixFilePermissions fileMode, DateTime modificationTime)
        => AddDirectory(name, ownerId, groupId, fileMode, modificationTime, modificationTime, modificationTime);

    void IFileSystemBuilder.AddFile(string name, byte[] content, int ownerId, int groupId, UnixFilePermissions fileMode, DateTime modificationTime)
        => AddFile(name, content, ownerId, groupId, fileMode, UnixFileType.Regular, modificationTime, modificationTime, modificationTime);

    void IFileSystemBuilder.AddFile(string name, Stream source, int ownerId, int groupId, UnixFilePermissions fileMode, DateTime modificationTime)
        => AddFile(name, source, ownerId, groupId, fileMode, UnixFileType.Regular, modificationTime, modificationTime, modificationTime);

    void IFileSystemBuilder.AddFile(string name, string sourcePath, int ownerId, int groupId, UnixFilePermissions fileMode, DateTime modificationTime)
        => AddFile(name, sourcePath, ownerId, groupId, fileMode, UnixFileType.Regular, modificationTime, modificationTime, modificationTime);
}
