namespace DiscUtils.ReFs.Entries.Files;

/// <summary>A data stream of a file record, with what is needed to read it.</summary>
internal readonly ref struct FileRecordStream
{
    /// <summary>The stored stream name; empty for the unnamed stream.</summary>
    public required string Name { get; init; }

    public ulong Size { get; init; }

    /// <summary>The data is stored in the record (<see cref="ResidentData" />) instead of in clusters (<see cref="RunTable" />).</summary>
    public bool IsResident { get; init; }

    /// <summary>The stream row's attribute word has <see cref="Enums.StreamAttributeFlags.Encrypted" />: the data on disk is ciphertext.</summary>
    public bool IsEncrypted { get; init; }

    public ReadOnlySpan<byte> ResidentData { get; init; }

    /// <summary>The embedded table of the stream's runs; empty when the stream has no clusters.</summary>
    public ReadOnlySpan<byte> RunTable { get; init; }

    /// <summary>
    ///     The version the run table is a delta of (see <see cref="FileRecord.TryFindVersion" />): ranges without a run row are read from that version.
    ///     Null for a named stream, which has one run table.
    /// </summary>
    public uint? ParentVersion { get; init; }
}