using DiscUtils.ReFs.Entries.Files;

namespace DiscUtils.ReFs.Entries.Enums;

/// <summary>What a row of a file record is, decided from its key (see <see cref="FileRecord.Classify" />).</summary>
internal enum FileRowKind
{
    /// <summary>A row this reader does not interpret.</summary>
    Other,

    /// <summary>The unnamed data stream, stored inline.</summary>
    UnnamedData,

    /// <summary>A named data stream: inline data, or the id of its run table.</summary>
    NamedData,

    ReparseData,

    /// <summary>A fragment of the extended attribute list.</summary>
    ExtendedAttributes,

    /// <summary>The runs of the unnamed stream.</summary>
    UnnamedRunTable,

    /// <summary>The runs of a named stream, matched by stream id.</summary>
    NamedRunTable,

    /// <summary>ReFS 3.1 to 3.5: the runs of the unnamed stream, in the row of attribute type 0x80.</summary>
    LegacyUnnamedRunTable,

    /// <summary>ReFS 3.1 to 3.5: one chunk of a named stream's inline attribute (attribute type 0xB0).</summary>
    LegacyNamedData
}