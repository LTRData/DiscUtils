using System;
using System.IO;

using DiscUtils.Streams;

namespace DiscUtils.VirtualFileSystem;
public sealed class VirtualFileSystemFile : VirtualFileSystemDirectoryEntry
{
    public override FileAttributes Attributes
    {
        get => base.Attributes & ~FileAttributes.Directory;
        set => base.Attributes = value & ~FileAttributes.Directory;
    }

    public VirtualFileSystemFile(VirtualFileSystemDirectory parent, string name, VirtualFileSystem.FileOpenDelegate open)
        : base(parent, name)
    {
        OpenFunc = open;
    }

    public VirtualFileSystemFile(VirtualFileSystemDirectory parent, string name, VirtualFileSystemFile existing_link)
        : this(parent, name, existing_link.OpenFunc)
    {
        Length = existing_link.Length;
        SectorSize = existing_link.SectorSize;
    }

    public VirtualFileSystem.FileOpenDelegate OpenFunc { get; }

    public SparseStream Open(FileMode mode, FileAccess access)
    {
        var stream = (OpenFunc?.Invoke(mode, access))
            ?? throw new NotSupportedException($"File '{FullPath}' cannot be opened");

        Length = stream.Length;

        if (stream is not SparseStream sparse)
        {
            sparse = SparseStream.FromStream(stream, Ownership.Dispose);
        }

        sparse.Disposing += (sender, e) => Length = sparse.Length;

        return sparse;
    }

    public long Length { get; set; }

    public int SectorSize { get; set; } = 4096;

    public long AllocationLength => Length + ((1 + ~(Length & (SectorSize - 1))) & (SectorSize - 1));

    public override long FileId => OpenFunc?.GetHashCode() ?? -1;

    public override VirtualFileSystemDirectoryEntry AddLink(VirtualFileSystemDirectory new_parent, string new_name)
        => new VirtualFileSystemFile(new_parent, new_name, this);
}
