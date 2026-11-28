namespace DiscUtils.ReFs.BTree;

/// <summary>Returns the sign of (entry key compared with the searched key): negative, zero or positive.</summary>
internal delegate int KeyComparison(ReadOnlySpan<byte> entryKey);
