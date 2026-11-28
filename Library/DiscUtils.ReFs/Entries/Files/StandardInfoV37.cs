using DiscUtils.ReFs.Compat;

namespace DiscUtils.ReFs.Entries.Files;

/// <summary>
///     The ReFS 3.7 and later layout (0x7C bytes; blocks on the test images are 0x80 with padding): the 3.4 layout plus
///     <list type="table">
///         <listheader>
///             <term>Offset, size</term><description>Field</description>
///         </listheader>
///         <item>
///             <term>0x74, 8</term><description>NextStreamSetId: allocator for stream set ids (base 0xF000), non-zero only on a file with an extra stream set.</description>
///         </item>
///     </list>
/// </summary>
internal sealed record StandardInfoV37 : StandardInfoV34
{
    public StandardInfoV37(ReadOnlySpan<byte> payload) : base(payload)
    {
        NextStreamSetId = payload.U64(116);
    }

    public ulong NextStreamSetId { get; }
}