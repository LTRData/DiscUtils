using DiscUtils.ReFs.Mount;

namespace DiscUtils.ReFs.BTree;

/// <summary>Iterative, lazy traversal of page-backed leaves, retaining only the active branch path.</summary>
internal struct TreeLeafCursor(PageReader pages, MetadataPage root, CancellationToken cancellationToken)
{
    private MetadataPage? _pending = root;
    private List<(MetadataPage Page, int Next)>? _parents;

    public MetadataPage Current { get; private set; } = null!;

    public bool MoveNext()
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_pending is { } page)
            {
                _pending = null;
                NodeView node = page.Node;

                if (node.IsLeaf)
                {
                    Current = page;
                    return true;
                }

                if (node.Count != 0)
                {
                    (_parents ??= []).Add((page, 0));
                }
            }

            if (_parents is not { Count: > 0 })
            {
                return false;
            }

            (MetadataPage parent, int next) = _parents[^1];
            NodeView branch = parent.Node;

            if (next == branch.Count)
            {
                _parents.RemoveAt(_parents.Count - 1);
                continue;
            }

            _parents[^1] = (parent, next + 1);
            _pending = pages.ReadChild(new PageRef(branch[next].Value), branch.Height, parent.IsPhysical);
        }
    }
}