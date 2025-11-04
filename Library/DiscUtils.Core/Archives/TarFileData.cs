using System.IO;

namespace DiscUtils.Archives;

public sealed class TarFileData
{
    public TarHeader Header { get; }

    private Stream? _source;

    public string Name => Header.FileName;

    public long Length => Header.FileLength;

    public TarFileData(TarHeader header, Stream? source)
    {
        Header = header;
        _source = source;
    }

    public Stream? GetStream() => _source;

    public override string ToString() => Name;
}
