using DiscUtils.ReFs.Compat;

namespace DiscUtils.ReFs.Entries.Files;

/// <summary>
///     The ReFS 3.4 layout (0x74 bytes): the common part plus
///     <list type="table">
///         <listheader>
///             <term>Offset, size</term><description>Field</description>
///         </listheader>
///         <item>
///             <term>0x58, 8</term>
///             <description>
///                 ChildOrdinal: the number assigned when the file was created in its directory (a directory's own row holds the highest handed out). Unique only within one
///                 directory, never reused. <c>ReFsEntry.FileId</c> reports it.
///             </description>
///         </item>
///         <item>
///             <term>0x60, 8</term>
///             <description>
///                 ExternalFileId2: the first half of a 16-byte extended id: a cross-volume identity, 0 for
///                 same-volume files. On ReFS 3.1 and 3.4 a file moved to another directory (its record relocated, logged as ReparentTable) keeps
///                 its old ordinal here, and Windows reports {0x68, 0x60} as the file's 128-bit id (lab images w2016- and w2019-dirtynames).
///             </description>
///         </item>
///         <item>
///             <term>0x68, 8</term><description>ExternalFileId3: the second half (the source parent directory id); 0 unless set as above.</description>
///         </item>
///         <item>
///             <term>0x70, 4</term>
///             <description>LinkCount: 1 in an embedded record; the real count only in a split-out (hard linked) backing record.</description>
///         </item>
///     </list>
/// </summary>
internal record StandardInfoV34 : StandardInformation
{
    public StandardInfoV34(ReadOnlySpan<byte> payload) : base(payload)
    {
        ChildOrdinal = payload.U64(88);
        ExternalFileId2 = payload.U64(96);
        ExternalFileId3 = payload.U64(104);
        LinkCount = payload.U32(112);
    }

    public ulong ChildOrdinal { get; }

    public ulong ExternalFileId2 { get; }

    public ulong ExternalFileId3 { get; }

    public uint LinkCount { get; }
}