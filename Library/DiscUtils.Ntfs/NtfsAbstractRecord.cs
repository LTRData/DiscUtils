using DiscUtils.Streams;
using DiscUtils.Vfs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiscUtils.Ntfs;


internal class NtfsAbstractRecord(NtfsFileSystem fileSystem, FileNameRecord Record, FileRecordReference FileIndex, String FilePath) : IAbstractRecord, IVfsFile {

	public DateTime CreationTimeUtc => Record.CreationTime;
	public FileAttributes FileAttributes => Record.FileAttributes;
	public string FileName => FilePath;
	public bool IsDirectory => Record.Flags.HasFlag(NtfsFileAttributes.Directory);
	public bool IsSymlink => Record.Flags.HasFlag(NtfsFileAttributes.ReparsePoint);
	public DateTime LastAccessTimeUtc => Record.LastAccessTime;
	public DateTime LastWriteTimeUtc => Record.ModificationTime;
	public long FileId => (long)FileIndex.Value;
	public long FileSize => (long)Record.RealSize;
	public File AsFile() => FileSystem.GetFile(FileIndex);
	public SparseStream FileContent => AsFile().OpenStream(AttributeType.Data, default, FileAccess.Read); // AttributeType.Data  attributeName=null

	protected NtfsFileSystem FileSystem { get; } = fileSystem;
	protected FileNameRecord Record { get; } = Record;
	protected FileRecordReference FileIndex { get; } = FileIndex;
	DateTime IVfsFile.CreationTimeUtc { get; set; }
	FileAttributes IVfsFile.FileAttributes { get; set; }
	IBuffer IVfsFile.FileContent { get; }
	long IVfsFile.FileLength { get; }
	DateTime IVfsFile.LastAccessTimeUtc { get; set; }
	DateTime IVfsFile.LastWriteTimeUtc { get; set; }

	public IAbstractDirectory GetAsAbstractDirectory() {

		if (!IsDirectory)
			throw new InvalidOperationException("Not a directory");
		var file = AsFile();
		if (file is Directory d)
			return new Directory.NtfsAbstractDirectory(FileSystem, Record, FileIndex, d,FilePath);
		throw new InvalidOperationException("fileSystem.GetFile() should have returned us as a Directory but we were not");
	}

	public VfsDirEntry GetAsDirEntry() => throw new NotImplementedException();
	public IVfsFile GetAsFile() => this;
	IEnumerable<StreamExtent> IVfsFile.EnumerateAllocationExtents() => this.GetAsFile().EnumerateAllocationExtents();
}
