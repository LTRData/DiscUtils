using DiscUtils.ReFs.Mount;

namespace DiscUtils.ReFs.BTree;

/// <summary>
///     The leaf rows of a node in key order, for <c>foreach</c> (see <see cref="TreeReader.Rows(DiscUtils.ReFs.Mount.PageRef)" />). The node may be a leaf (the usual case for an
///     embedded table: nothing is allocated) or have branch levels, whose child pages are read as the enumeration reaches them, so leaving the loop early stops the reads.
/// </summary>
internal ref struct TreeRows
{
    private readonly PageReader _pages;
    private readonly NodeView _root;
    private readonly CancellationToken _cancellationToken;

    /// <summary>The node being read, and the next entry in it.</summary>
    private NodeView _node;

    private int _next;

    private TreeLeafCursor _leaves;
    private bool _hasLeaves;
    private readonly bool _embeddedRoot;
    private int _nextChild;

    internal TreeRows(PageReader pages, ReadOnlySpan<byte> buffer, int nodeOffset, MetadataPage? page = null, CancellationToken cancellationToken = default)
    {
        _pages = pages;
        _root = new NodeView(buffer, nodeOffset);
        _node = _root;
        Page = page;
        _cancellationToken = cancellationToken;
        _embeddedRoot = page is null;

        if (!_root.IsLeaf && page is not null)
        {
            _leaves = new TreeLeafCursor(pages, page, cancellationToken);
            _hasLeaves = true;
        }
    }

    public EntryView Current { get; private set; }

    /// <summary>The page <see cref="_node" /> lives in; null for an embedded root.</summary>
    public MetadataPage? Page { get; private set; }

    public readonly int Slot => _next - 1;

    public readonly TreeRows GetEnumerator()
    {
        return this;
    }

    public bool MoveNext()
    {
        while (true)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            if (_node.IsLeaf && _next < _node.Count)
            {
                Current = _node[_next++];
                return true;
            }

            if (_root.IsLeaf)
            {
                return false;
            }

            if (_hasLeaves && _leaves.MoveNext())
            {
                Page = _leaves.Current;
                _node = Page.Node;
                _next = 0;
                continue;
            }

            _hasLeaves = false;
            if (!_embeddedRoot || _nextChild == _root.Count)
            {
                return false;
            }

            MetadataPage child = _pages.ReadChild(new PageRef(_root[_nextChild++].Value), _root.Height);
            _leaves = new TreeLeafCursor(_pages, child, _cancellationToken);
            _hasLeaves = true;
        }
    }
}