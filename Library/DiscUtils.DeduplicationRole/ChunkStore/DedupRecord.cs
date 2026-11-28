namespace DiscUtils.DeduplicationRole.ChunkStore;

/// <summary>A "Ckhr" record of a chunk store container: its header and its payload (checked against the header's CRC-64 when verifying).</summary>
internal sealed class DedupRecord(DedupRecordHeader header, byte[] payload)
{
    public DedupRecordHeader Header { get; } = header;

    public byte[] Payload { get; } = payload;
}
