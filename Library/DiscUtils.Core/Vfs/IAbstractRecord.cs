using System;
using System.Collections.Generic;
using System.IO;

namespace DiscUtils.Vfs;


public interface IAbstractRecord {
		DateTime CreationTimeUtc { get; }
		FileAttributes FileAttributes { get; }
	/// <summary>
	/// the SubPath relative to the IAbstractDirectory that returned it
	/// </summary>
		string FileName { get; }
		bool IsDirectory { get; }
		bool IsSymlink { get; }
		DateTime LastAccessTimeUtc { get; }
		DateTime LastWriteTimeUtc { get; }
		long FileId { get; }
		long FileSize { get; }
		Streams.SparseStream FileContent { get; }
		VfsDirEntry GetAsDirEntry();
		IVfsFile GetAsFile();
		IAbstractDirectory GetAsAbstractDirectory();
}

public interface IAbstractDirectory : IAbstractRecord {
		IEnumerable<IAbstractRecord> AllEntries { get; }
}
