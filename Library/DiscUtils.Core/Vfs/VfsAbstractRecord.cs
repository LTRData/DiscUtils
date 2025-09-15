using DiscUtils.Streams;
using DiscUtils.Vfs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiscUtils.Vfs;

public class VfsAbstractRecord(VfsDirEntry DirEntry, IVfsFile File) : IAbstractRecord {
	public DateTime CreationTimeUtc => File.CreationTimeUtc;
	public FileAttributes FileAttributes => File.FileAttributes;
	public string FileName => DirEntry.FileName;
	public bool IsDirectory => DirEntry.IsDirectory;
	public bool IsSymlink => DirEntry.IsSymlink;
	public DateTime LastAccessTimeUtc => File.LastAccessTimeUtc;
	public DateTime LastWriteTimeUtc => File.LastWriteTimeUtc;
	public long FileId => DirEntry.UniqueCacheId;
	public long FileSize => File.FileLength;
	public SparseStream FileContent => new BufferStream(File.FileContent, FileAccess.Read);

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
