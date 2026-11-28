#if NET46
using DiscUtils.Streams.Compatibility;
#endif

namespace DiscUtils.ReFs.Replay;

/// <summary>Replayed rows of one table, by exact key bytes.</summary>
internal sealed class TableOverlay
{
    private readonly Dictionary<byte[], OverlayRow> _rows = new Dictionary<byte[], OverlayRow>(ByteArrayComparer.Instance);

    public IReadOnlyDictionary<byte[], OverlayRow> Rows => _rows;

    public bool TryGet(ReadOnlySpan<byte> key, out OverlayRow? row) => _rows.TryGetValue(key.ToArray(), out row);

    internal void Set(byte[] key, OverlayRow row) => _rows[key] = row;

    /// <summary>Compares row keys by their bytes: equality, and order as unsigned bytes.</summary>
    internal sealed class ByteArrayComparer : IEqualityComparer<byte[]>, IComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new ByteArrayComparer();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);

        public bool Equals(byte[]? x, byte[]? y) => x is not null && y is not null && x.AsSpan().SequenceEqual(y);

        public int GetHashCode(byte[] obj)
        {
            HashCode hash = default;

            foreach (var b in obj)
            {
                hash.Add(b);
            }

            return hash.ToHashCode();
        }
    }
}