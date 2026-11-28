namespace DiscUtils.ReFs.BTree.Editing;

/// <summary>Reads a row of an on-disk table by exact key (a full scan); null when the row does not exist.</summary>
internal delegate byte[]? BaseRowReader(ulong objectId, byte[] key);