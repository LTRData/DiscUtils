namespace DiscUtils.ReFs.BTree;

/// <summary>Receives a leaf entry found by key, with the page and slot it lives in.</summary>
internal delegate void FoundEntryVisitor(EntryView entry, MetadataPage page, int slot);
