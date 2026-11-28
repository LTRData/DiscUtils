using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.IO;
using DiscUtils.ReFs.Mount;
using DiscUtils.ReFs.SystemTables;

namespace DiscUtils.ReFs.BTree;

/// <summary>Reads and verifies metadata pages addressed by <see cref="PageRef" />, normally in container space.</summary>
/// <remarks>
///     Pages of a read-only volume never change, so verified pages are kept in a bounded LRU cache
///     (<paramref name="cacheCapacity" /> pages; 0 disables it). Pages that fail verification are never cached.
/// </remarks>
internal sealed class PageReader(IBlockSource volume, uint clusterSize, bool verifyChecksums, int cacheCapacity, ILogger logger)
{
    private readonly Dictionary<(PageRef Pointer, bool Physical), LinkedListNode<MetadataPage>> _cache = [];
    private readonly object _cacheLock = new object();
    private readonly LinkedList<MetadataPage> _recent = new LinkedList<MetadataPage>();

    private long _pagesRead;

    public ContainerMap Map
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            lock (_cacheLock)
            {
                field = value;
                _cache.Clear();
                _recent.Clear();
            }
        }
    } = ContainerMap.IdentityOnly;

    public uint ClusterSize { get; } = clusterSize;

    public bool VerifyChecksums => verifyChecksums;

    /// <summary>
    ///     Asked when a page fails its checksum: returns a sentence for the error when the volume has changed on disk since it was
    ///     opened (a volume in use), else null. Only the message changes; the read still fails.
    /// </summary>
    public Func<string?>? StaleViewCheck { get; set; }

    /// <summary>Number of pages currently cached.</summary>
    public int CachedPages
    {
        get
        {
            lock (_cacheLock)
            {
                return _cache.Count;
            }
        }
    }

    /// <summary>Number of metadata pages read from the volume (cache misses), each verified when checksums are on.</summary>
    public long PagesRead => Interlocked.Read(ref _pagesRead);

    public MetadataPage ReadChild(PageRef pointer, byte parentHeight, bool physical = false)
    {
        MetadataPage child = Read(pointer, physical);
        if (child.Node.Height >= parentHeight)
        {
            throw new ReFsFormatException($"Metadata page at cluster 0x{pointer.Lcn0:X} does not descend below parent level {parentHeight}.");
        }

        return child;
    }

    public MetadataPage Read(PageRef pointer, bool physical = false)
    {
        if (cacheCapacity <= 0)
        {
            return ReadUncached(pointer, physical);
        }

        (PageRef, bool) key = (pointer, physical);

        lock (_cacheLock)
        {
            if (_cache.TryGetValue(key, out LinkedListNode<MetadataPage>? hit))
            {
                _recent.Remove(hit);
                _recent.AddFirst(hit);
                Logs.PageRead(logger, pointer.Lcn0, pointer.ClusterCount, pointer.Kind, "cache hit");

                return hit.Value;
            }
        }

        MetadataPage page = ReadUncached(pointer, physical);

        lock (_cacheLock)
        {
            if (!_cache.ContainsKey(key))
            {
                _cache[key] = _recent.AddFirst(page);

                if (_cache.Count > cacheCapacity)
                {
                    LinkedListNode<MetadataPage> oldest = _recent.Last!;
                    _recent.RemoveLast();
                    _cache.Remove((oldest.Value.Source, oldest.Value.IsPhysical));
                }
            }
        }

        return page;
    }

    private MetadataPage ReadUncached(PageRef pointer, bool physical)
    {
        Interlocked.Increment(ref _pagesRead);
        int clusters = pointer.ClusterCount;
        byte[] data = new byte[clusters * (int)ClusterSize];
        ContainerMap map = physical ? ContainerMap.IdentityOnly : Map;

        // One device request for each run of consecutive clusters (a page normally is one run): reading it cluster by cluster is
        // several small requests, which on a spinning disk or a raw device costs a seek or a rotation each.
        int index = 0;

        while (index < clusters)
        {
            ulong first = map.Translate(pointer.GetLcn(index));
            int run = 1;
            while (index + run < clusters && map.Translate(pointer.GetLcn(index + run)) == first + (ulong)run)
            {
                run++;
            }

            volume.ReadExactly(BlockSourceExtensions.ClusterOffset(first, ClusterSize), data.AsSpan(index * (int)ClusterSize, run * (int)ClusterSize));
            index += run;
        }

        if (verifyChecksums && !pointer.Verify(data))
        {
            Logs.PageChecksumFailed(logger, pointer.Lcn0, pointer.Kind);
            throw new ReFsCorruptionException(pointer.Lcn0, pointer.Kind, staleView: StaleViewCheck?.Invoke());
        }


        Logs.PageRead(logger, pointer.Lcn0, pointer.ClusterCount, pointer.Kind, verifyChecksums ? "read and verified" : "read");

        return new MetadataPage(data, pointer, physical);
    }
}