namespace DiscUtils.ReFs.BTree.Editing;

/// <summary>Reads every row of an embedded table whose node (at <paramref name="nodeOffset" /> of the row value) is a branch.</summary>
internal delegate List<EmbeddedRow> EmbeddedRowReader(ReadOnlySpan<byte> value, int nodeOffset);