using DiscUtils.ReFs.Mount;

namespace DiscUtils.ReFs.BTree;

/// <summary>Traversal and keyed search of B+trees stored in metadata pages.</summary>
internal sealed class TreeReader(PageReader pages)
{
    public PageReader Pages { get; } = pages;

    /// <summary>
    ///     The shortest standard information block of this volume (<see cref="Entries.Files.StandardInformation.MinimumLengthFor" />): 0x74 bytes below ReFS 3.7,
    ///     0x7C from 3.7 on. Kept here because every file record is read through a tree reader.
    /// </summary>
    public int StandardInformationMinimumLength { get; init; } = Entries.Files.StandardInformation.MinimumLength;

    /// <summary>The leaf rows of the tree under a root page, in key order, for <c>foreach</c>. Pages are read as the enumeration reaches them, so a <c>break</c> stops the reads.</summary>
    public TreeRows Rows(PageRef root)
    {
        return Rows(root, CancellationToken.None);
    }

    public TreeRows Rows(PageRef root, CancellationToken cancellationToken, bool physical = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MetadataPage page = Pages.Read(root, physical);
        return new TreeRows(Pages, page.Data.Span, page.NodeOffset, page, cancellationToken);
    }

    /// <summary>The leaf rows of a node inside any buffer (a page, or an embedded table value) in key order, for <c>foreach</c>.</summary>
    public TreeRows Rows(ReadOnlySpan<byte> buffer, int nodeOffset)
    {
        return new TreeRows(Pages, buffer, nodeOffset);
    }

    public IEnumerable<MetadataPage> LeafPages(PageRef root, CancellationToken cancellationToken, bool physical = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TreeLeafCursor leaves = new TreeLeafCursor(Pages, Pages.Read(root, physical), cancellationToken);
        while (leaves.MoveNext())
        {
            yield return leaves.Current;
        }
    }

    /// <summary>
    ///     Finds the first leaf entry for which <paramref name="compare" /> returns 0. Branch entries hold the inclusive
    ///     upper bound of their child (the last entry means "everything above"). Returns false when no entry matches
    ///     (which also happens if the supplied ordering disagrees with the tree's real ordering).
    /// </summary>
    public bool TryFind(PageRef root, KeyComparison compare, FoundEntryVisitor onFound, Action<MetadataPage>? validatePage = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return FindInPage(Pages.Read(root), compare, onFound, validatePage, cancellationToken);
    }

    private bool FindInPage(MetadataPage page, KeyComparison compare, FoundEntryVisitor onFound, Action<MetadataPage>? validatePage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        validatePage?.Invoke(page);
        NodeView node = page.Node;
        if (node.Count == 0)
        {
            return false;
        }

        if (node.IsLeaf)
        {
            // Lower bound: the first matching row, so a comparison that treats
            // several rows as equal (folded names) finds the leftmost one.
            int low = 0;
            int high = node.Count;

            while (low < high)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int mid = low + (high - low) / 2;

                if (compare(node[mid].Key) < 0)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            if (low == node.Count || compare(node[low].Key) != 0)
            {
                return false;
            }

            onFound(node[low], page, low);
            return true;
        }

        // Branch: first child whose upper bound is >= the searched key. The last entry (flag 0x2 on native pages, whose
        // stored key may be non-empty) is the catch-all child and is never compared.
        int child = node.Count - 1;
        bool equalBound = false;

        for (int i = 0; i < node.Count - 1; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EntryView entry = node[i];

            // An empty separator is the smallest key, never the upper bound of a searched key: Windows has no special case for it and
            // compares it as the shortest string, so the search moves past it. Only a split with an empty lower key can write one, which
            // needs two empty names in one table; no image has shown it. Windows would descend there for an empty searched key, which no
            // caller of this reader searches for.
            if (entry.Key.IsEmpty)
            {
                continue;
            }

            int order = compare(entry.Key);

            if (order >= 0)
            {
                child = i;
                equalBound = order == 0;
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (FindInPage(Pages.ReadChild(new PageRef(node[child].Value), node.Height), compare, onFound, validatePage, cancellationToken))
        {
            return true;
        }

        // A separator equal to the key might be an exclusive bound: also try the next child.
        return equalBound && child + 1 < node.Count
                          && FindInPage(Pages.ReadChild(new PageRef(node[child + 1].Value), node.Height), compare, onFound, validatePage, cancellationToken);
    }
}