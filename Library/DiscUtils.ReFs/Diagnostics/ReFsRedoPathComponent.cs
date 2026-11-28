using DiscUtils.ReFs.Compat;

namespace DiscUtils.ReFs.Diagnostics;

/// <summary>One component of a redo record's table path.</summary>
/// <param name="Kind">The component kind (<see cref="RootKind"/> for the table's object id).</param>
/// <param name="Key">The key bytes.</param>
public readonly record struct ReFsRedoPathComponent(uint Kind, ReadOnlyMemory<byte> Key)
{
    /// <summary>The kind of the root component, whose key holds the object id.</summary>
    public const uint RootKind = 0xE030;

    /// <summary>For a root component: the object id of the table; 0 otherwise.</summary>
    public ulong ObjectId => Kind == RootKind && Key.Length >= 16 ? Key.Span.U64(8) : 0;
}