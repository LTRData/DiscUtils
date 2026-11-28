using DiscUtils.ReFs.Entries;
using DiscUtils.Vfs;

namespace DiscUtils.ReFs;

/// <summary>A ReFS directory; its table is read on first use.</summary>
internal sealed class ReFsDirectory : ReFsFile, IVfsDirectory<ReFsDirEntry, ReFsFile>
{
    private Dictionary<string, ReFsDirEntry>? _entries;

    public ReFsDirectory(ReFsContext context, ReFsEntry entry)
        : base(context, entry) { }

    public IReadOnlyDictionary<string, ReFsDirEntry> AllEntries
    {
        get
        {
            if (_entries is null)
            {
                // Ordinal: a case-sensitive directory may hold names differing only by case.
                var entries = new Dictionary<string, ReFsDirEntry>(StringComparer.Ordinal);
                foreach (var child in Context.Volume.Enumerate(Entry))
                {
                    entries[child.Name] = new ReFsDirEntry(child, Context.NextCacheId());
                }

                _entries = entries;
            }

            return _entries;
        }
    }

    public ReFsDirEntry Self => new(Entry, Context.NextCacheId());

    public ReFsDirEntry GetEntryByName(string name)
    {
        if (_entries is not null && _entries.TryGetValue(name, out var cached))
        {
            return cached;
        }

        // "file:stream" addresses a named stream of the file; ':' cannot occur in a ReFS file name.
        var separator = name.IndexOf(':');
        var fileName = separator >= 0 ? name.Substring(0, separator) : name;

        if (separator >= 0 && fileName.Length > 0)
        {
            return GetEntryByName(fileName);
        }

        // Name matching follows the volume's stored upcase table, so ask the volume.
        if (Context.Volume.FindChild(Entry, fileName) is { } child)
        {
            return new ReFsDirEntry(child, Context.NextCacheId());
        }

        return null!;
    }

    public ReFsDirEntry CreateNewFile(string name) => throw new NotSupportedException();
}