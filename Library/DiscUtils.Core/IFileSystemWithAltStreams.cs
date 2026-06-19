using System.Collections.Generic;

namespace DiscUtils;

public interface IFileSystemWithAltStreams : IFileSystem
{
    IEnumerable<string> GetAlternateDataStreams(string path);
}
