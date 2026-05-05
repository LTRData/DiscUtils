using System.IO;
using DiscUtils.Streams;
using DiscUtils.Vfs;

namespace DiscUtils.HfsPlus;

internal class Symlink : File, IVfsSymlink<DirEntry, File>
{
    public Symlink(Context context, CatalogNodeId nodeId, CommonCatalogFileInfo catalogInfo)
        : base(context, nodeId, catalogInfo) { }

    public string TargetPath
    {
        get
        {
            if (field == null)
            {
                using var stream = new BufferStream(FileContent, FileAccess.Read);
                using var reader = new StreamReader(stream);
                field = reader.ReadToEnd();
                field = field.Replace('/', Path.DirectorySeparatorChar);
            }

            return field;
        }

        private set;
    }
}