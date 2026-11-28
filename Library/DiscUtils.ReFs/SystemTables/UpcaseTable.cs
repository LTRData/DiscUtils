using System.Globalization;
using DiscUtils.ReFs.BTree;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Mount;

namespace DiscUtils.ReFs.SystemTables;

/// <summary>
///     The volume's case-folding table (objects 0x8 and 0x7, a failover pair; 0x8 is read, 0x7 when 0x8 has a damaged page): 65,536 UTF-16 code units mapping each unit to its
///     upper-case form. On disk the table is one 131,072-byte little-endian stream cut into rows keyed {u32 index}:
///     row 0 = name "Upcase Table", row 1 = {u32 entry count}, rows 2.. = consecutive slices of the stream
///     (341 bytes each, so a slice can split a code unit; the last one is shorter). Names are compared
///     code unit by code unit after folding; no surrogate handling, like Windows.
/// </summary>
internal sealed class UpcaseTable
{
    public const ulong ObjectId = 0x7;

    /// <summary>
    /// The copy Windows reads. Identifiers {8, 7} form one failover pair; the first identifier is the active table.
    /// </summary>
    public const ulong MirrorObjectId = 0x8;

    public const int EntryCount = 0x10000;

    private const uint CountRow = 1;
    private const uint FirstDataRow = 2;

    private readonly char[] _map;

    private UpcaseTable(char[] map)
    {
        _map = map;
    }

    /// <summary>
    ///     Non-authoritative invariant mapping for callers without a volume context. Loading an actual volume
    ///     requires its stored table; this mapping must not conceal a missing table.
    /// </summary>
    public static UpcaseTable Invariant { get; } = BuildInvariant();

    /// <summary>
    /// Reads the table from object 0x8, the active copy. This reader reads 0x7 when 0x8 has no record.
    /// When 0x8 exists but a page of it fails its checksum, 0x7 is read, as Windows does with the partner of a failover pair. A malformed table (wrong rows or count) is not retried: that is not a page failure.
    /// </summary>
    /// <exception cref="ReFsFormatException">Neither object exists, or the table read is malformed.</exception>
    /// <exception cref="ReFsCorruptionException">Every existing copy has a damaged page.</exception>
    public static UpcaseTable Load(TreeReader tree, ObjectTable objects, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        PageRef partner;

        if (!objects.TryGetRoot(MirrorObjectId, out PageRef active, cancellationToken))
        {
            if (!objects.TryGetRoot(ObjectId, out partner, cancellationToken))
            {
                throw new ReFsFormatException("The volume's upcase table (objects 0x8 and 0x7) is missing; authoritative name comparison is unavailable.");
            }

            return Read(tree, partner, cancellationToken);
        }

        try
        {
            return Read(tree, active, cancellationToken);
        }
        catch (ReFsCorruptionException ex) when (objects.TryGetRoot(ObjectId, out partner, cancellationToken))
        {
            Logs.MirroredTableFailover(logger ?? NullLogger.Instance, "upcase", MirrorObjectId, ex.Kind, ex.Lcn, ObjectId);
            return Read(tree, partner, cancellationToken);
        }
    }

    /// <summary>
    ///     Reads the table as Windows does: the count row (row 1, a value of at least 4 bytes, the u32 entry count; 0 is refused), then the data rows from
    ///     row 2 appended in key order until 2 x count bytes are there (anything else is refused). Windows then uses the first 0x10000 entries and maps any
    ///     it lacks to themselves; every volume seen has exactly 0x10000.
    /// </summary>
    private static UpcaseTable Read(TreeReader tree, PageRef root, CancellationToken cancellationToken)
    {
        uint? count = null;
        List<byte[]> data = [];
        long length = 0;

        foreach (EntryView entry in tree.Rows(root, cancellationToken))
        {
            if (entry.Key.Length != 4)
            {
                continue;
            }

            uint row = entry.Key.U32(0);

            if (row == CountRow && entry.Value.Length >= 4)
            {
                count = entry.Value.U32(0);
            }
            else if (row >= FirstDataRow)
            {
                data.Add(entry.Value.ToArray());
                length += entry.Value.Length;
            }
        }

        if (count is not { } entries || entries == 0 || length != 2L * entries)
        {
            throw new ReFsFormatException($"Upcase table has {count?.ToString(CultureInfo.InvariantCulture) ?? "no"} entries and {length} bytes; the data must be two bytes per entry.");
        }

        byte[] stream = new byte[length];
        int offset = 0;
        foreach (byte[] chunk in data)
        {
            chunk.CopyTo(stream, offset);
            offset += chunk.Length;
        }

        char[] map = Identity();
        int used = (int)Math.Min(entries, EntryCount);

        for (int i = 0; i < used; i++)
        {
            if ((i & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            map[i] = (char)stream.AsSpan(2 * i).U16(0);
        }

        return new UpcaseTable(map);
    }

    /// <summary>Builds a table from explicit mappings (tests); every other unit maps to itself.</summary>
    public static UpcaseTable FromMappings(IEnumerable<KeyValuePair<char, char>> mappings)
    {
        char[] map = Identity();
        foreach ((char from, char to) in mappings)
        {
            map[from] = to;
        }

        return new UpcaseTable(map);
    }

    public char Fold(char c)
    {
        return _map[c];
    }

    public string Upper(string name)
    {
        var chars = new char[name.Length];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = _map[name[i]];
        }

        return new string(chars);
    }

    public bool Equal(string a, string b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (_map[a[i]] != _map[b[i]])
            {
                return false;
            }
        }

        return true;
    }

    private static char[] Identity()
    {
        char[] map = new char[EntryCount];
        for (int i = 0; i < map.Length; i++)
        {
            map[i] = (char)i;
        }

        return map;
    }

    private static UpcaseTable BuildInvariant()
    {
        char[] map = Identity();
        for (int i = 0; i < map.Length; i++)
        {
            map[i] = char.ToUpperInvariant((char)i);
        }

        return new UpcaseTable(map);
    }
}