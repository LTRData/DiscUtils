using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DiscUtils.Vfs;

internal class FullFile(VfsDirEntry DirEntry, IVfsFile File) : IAbstractRecord {
	public DateTime CreationTimeUtc => File.CreationTimeUtc;
	public FileAttributes FileAttributes => File.FileAttributes;
	public string FileName => DirEntry.FileName;
	public bool IsDirectory => DirEntry.IsDirectory;
	public bool IsSymlink => DirEntry.IsSymlink;
	public DateTime LastAccessTimeUtc => File.LastAccessTimeUtc;
	public DateTime LastWriteTimeUtc => File.LastWriteTimeUtc;
	public long FileId => DirEntry.UniqueCacheId;
	public long FileSize => File.FileLength;
	public Streams.IBuffer FileContent => File.FileContent;

	public IAbstractDirectory GetAsAbstractDirectory() {
		if (! IsDirectory) 
			throw new InvalidOperationException("Not a directory");
		if (this is IAbstractDirectory dir)
			return dir;
		throw new InvalidOperationException("We are not an instance of a directory but should be");
	}
	public VfsDirEntry GetAsDirEntry() => DirEntry;
	public IVfsFile GetAsFile() => File;


}
public interface IAbstractRecord {
		DateTime CreationTimeUtc { get; }
		FileAttributes FileAttributes { get; }
		string FileName { get; }
		bool IsDirectory { get; }
		bool IsSymlink { get; }
		DateTime LastAccessTimeUtc { get; }
		DateTime LastWriteTimeUtc { get; }
		long FileId { get; }
		long FileSize { get; }
		Streams.IBuffer FileContent { get; }
		VfsDirEntry GetAsDirEntry();
		IVfsFile GetAsFile();
		IAbstractDirectory GetAsAbstractDirectory();
}

public interface IAbstractDirectory : IAbstractRecord {
		IEnumerable<IAbstractRecord> AllEntries { get; }
}
