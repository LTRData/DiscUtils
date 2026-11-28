using DiscUtils.ReFs.Compat;

namespace DiscUtils.ReFs.Replay;

/// <summary>One component of a record's table path: {u32 kind, u32 property id, u32 0, key bytes}. The root component (kind 0xE030) holds the 16-byte object id.</summary>
internal readonly record struct RedoPathComponent(uint Kind, uint Property, ReadOnlyMemory<byte> Key)
{
    public const uint RootKind = 0xE030;

    /// <summary>True when this is a well-formed root component carrying a 16-byte well-known object id.</summary>
    public bool IsRoot => Kind == RootKind && Key.Length == 16 && Key.Span.U64(0) == 0;

    /// <summary>For a root component: the object id (qword 1 is the id of directories and tables; qword 0 is 0 for well-known ids).</summary>
    public ulong ObjectId => Key.Length >= 16 ? Key.Span.U64(8) : 0;

    /// <summary>
    ///     Parses a binary redo table path ({u32 count, u32 descriptorsOffset (8), {u32 offset, u32 size}[count],
    ///     components {u32 kind, u32 property, u32 0, key}}) with exactly <paramref name="expectedCount" /> components.
    /// </summary>
    public static bool TryParsePath(ReadOnlySpan<byte> data, int expectedCount, out RedoPathComponent[] components)
    {
        components = [];

        if (data.Length < 8 + (8 * expectedCount) || data.U32(0) != (uint)expectedCount || data.U32(4) != 8)
        {
            return false;
        }

        var result = new RedoPathComponent[expectedCount];

        for (var i = 0; i < expectedCount; i++)
        {
            var offset = data.U32(8 + (8 * i));
            var size = data.U32(12 + (8 * i));

            if (size < 12 || (ulong)offset + size > (ulong)data.Length)
            {
                return false;
            }

            var start = (int)offset;
            result[i] = new RedoPathComponent(data.U32(start), data.U32(start + 4), data.Slice(start + 12, (int)size - 12).ToArray());
        }

        components = result;
        return true;
    }
}