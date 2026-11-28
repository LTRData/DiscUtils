namespace DiscUtils.ReFs.BTree.Enums;

/// <summary>
/// The flags byte of a B+tree node header. Evidence: the values on every node of three test
/// images (4 KiB and 64 KiB clusters; pages of the global tables and of every object, and the embedded tables inside rows): 0x00, 0x01, 0x02, 0x03,
/// 0x04, 0x06, 0x07, 0x0C, 0x0E and 0x0F occur.
/// </summary>
[Flags]
internal enum NodeProperties : byte
{
    None = 0,

    /// <summary>
    ///     An interior node: its entries point at child pages. The level byte is 1 to 0xF7 when this is set and 0 when it
    ///     is clear; on the images it is set on exactly the nodes with a level above 0.
    /// </summary>
    Interior = 0x01,

    /// <summary>
    ///     The root node of its tree. On the images it is set on exactly the root pages (the ones the checkpoint and the object table point at) and on every
    ///     embedded table (which is its own root), and clear on every other page. When a root is pushed down by a new root, the old root is inferred to lose this flag while the new root gets <see cref="Interior" />.
    /// </summary>
    Root = 0x02,

    /// <summary>
    /// A property of the table, not of the node: it is set when the table's definition has bit 0x02. On the images some
    /// global tables have it on all their pages and the tables nested inside file records (the run tables) have it, while object tables (directories)
    /// and file records never do: tables whose rows are extents. The name is inferred.
    /// </summary>
    Stream = 0x04,

    /// <summary>
    ///     The slot array carries a valid key index: the second u16 of each slot is the row's start value minus the base at 0x18 of the header.
    ///     Only on <see cref="Stream" /> tables. It is inferred to be set after that index is filled
    ///     and cleared whenever the node's entries are moved, split, merged or
    ///     rolled back, so it is per-node state. The name is inferred.
    /// </summary>
    KeyIndexValid = 0x08,

    /// <summary>
    ///     Not an on-disk flag and never written to disk: set by this reader on a node it builds in memory when the node is larger than
    ///     64 KiB (a large run table rebuilt from a branch during redo-log replay). Each slot is then one 32-bit entry offset instead of
    ///     {u16 offset, u16 key index}. A node read from disk that carried the bit would be read the same way, bounds-checked like any other.
    /// </summary>
    WideSlots = 0x80
}