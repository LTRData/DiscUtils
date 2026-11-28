using DiscUtils.ReFs.Mount;

namespace DiscUtils.ReFs.BTree;

/// <summary>
/// A verified metadata page of a B+tree (16 KiB, or one 64 KiB cluster): the common header, then at 0x50 the index root (<see cref="IndexRoot" />:
/// the table's descriptor on a root page, only the node offset on any other page), the table's payload when it is a root, and the node (<see
/// cref="NodeView" />).
/// </summary>
internal sealed record MetadataPage : MetadataPageHeader
{
    private const uint Magic = 0x2B42534D; // "MSB+"

    internal MetadataPage(ReadOnlyMemory<byte> data, PageRef source, bool physical = false) : base(data.Span)
    {
        Data = data;
        Source = source;
        IsPhysical = physical;

        if (Signature != Magic) // 0x00
        {
            throw new ReFsFormatException($"Metadata page at cluster 0x{source.Lcn0:X} has no 'MSB+' header.");
        }

        if (data.Length < HeaderLength + IndexRoot.MinimumLength)
        {
            throw new ReFsFormatException($"Metadata page at cluster 0x{source.Lcn0:X} is too short for its index root.");
        }

        Root = new IndexRoot(data.Span[HeaderLength..]); // 0x50

        long nodeOffset = HeaderLength + (long)Root.NodeOffset;
        if (nodeOffset + NodeView.HeaderSize > data.Length)
        {
            throw new ReFsFormatException($"Metadata page at cluster 0x{source.Lcn0:X}: node offset out of range.");
        }

        NodeOffset = (int)nodeOffset;
    }

    /// <summary>The reference this page was read through.</summary>
    public PageRef Source { get; }

    /// <summary>The owning tree uses physical clusters rather than container-space clusters.</summary>
    internal bool IsPhysical { get; }

    internal ReadOnlyMemory<byte> Data { get; }

    /// <summary>The index root at 0x50; it has the table's descriptor only when this page is the root of its table.</summary>
    public IndexRoot Root { get; }

    /// <summary>Offset of the node header within <see cref="Data" />.</summary>
    public int NodeOffset { get; }

    public NodeView Node => new NodeView(Data.Span, NodeOffset);
}