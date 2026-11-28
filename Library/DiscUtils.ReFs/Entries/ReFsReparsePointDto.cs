using System.Diagnostics.CodeAnalysis;
using System.Text;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Entries.Enums;

namespace DiscUtils.ReFs.Entries;

/// <summary>
///     Reparse data of a file as reported to callers (symbolic link, mount point, or another tag): a decoded view of the on-disk
///     <c>$REPARSE_POINT</c> row (<c>ReparsePoint</c>). For symlinks and mount points the substitute and print names are decoded;
///     <see cref="IsRelative" /> is the symlink "relative" flag. A third-party tag (one without the Microsoft bit, 0x80000000) carries the GUID of the
///     product that owns it in <see cref="ReparseGuid" />.
/// </summary>
internal sealed record ReFsReparsePointDto(uint Tag, string? SubstituteName, string? PrintName, bool IsRelative, Guid? ReparseGuid = null)
{
    public const uint SymbolicLinkTag = 0xA000000C;
    public const uint MountPointTag = 0xA0000003;

    /// <summary>The raw reparse data (the buffer after the tag), as stored.</summary>
    public byte[] Data { get; init; } = [];

    /// <summary>
    ///     The tag belongs to a third party, not Microsoft (its Microsoft bit, 0x80000000, is clear). Its reparse buffer is a
    ///     <c>REPARSE_GUID_DATA_BUFFER</c>, whose owner GUID is <see cref="ReparseGuid" />.
    /// </summary>
    public bool IsThirdParty => !((ReparseTagFlags)Tag).HasFlag(ReparseTagFlags.Microsoft);

    /// <summary>Decodes a reparse point row; null when the row is malformed or a symlink or mount point has unreadable names.</summary>
    internal static ReFsReparsePointDto? From(Files.ReparsePoint? point)
    {
        if (point is null)
        {
            return null;
        }

        var dto = point.Tag switch
        {
            SymbolicLinkTag => ParseNames(point.Tag, point.Data, 4),
            MountPointTag => ParseNames(point.Tag, point.Data, 0),
            _ => new ReFsReparsePointDto(point.Tag, null, null, false, point.ReparseGuid)
        };

        return dto is null ? null : dto with { Data = point.Data };
    }

    private static ReFsReparsePointDto? ParseNames(uint tag, ReadOnlySpan<byte> data, int flagsLength)
    {
        const int OffsetsLength = 8;
        var pathStart = OffsetsLength + flagsLength;

        if (data.Length < pathStart)
        {
            return null;
        }

        var path = data[pathStart..];

        if (!TryReadName(path, data.U16(0), data.U16(2), out var substitute) || !TryReadName(path, data.U16(4), data.U16(6), out var print))
        {
            return null;
        }

        var relative = flagsLength == 4 && ((SymbolicLinkFlags)data.U32(OffsetsLength)).HasFlag(SymbolicLinkFlags.Relative);
        return new ReFsReparsePointDto(tag, substitute, print, relative);
    }

    /// <summary>Reads a UTF-16 name of the path buffer; false when its length is odd or it lies outside the buffer.</summary>
    private static bool TryReadName(ReadOnlySpan<byte> path, ushort offset, ushort length, [NotNullWhen(true)] out string? name)
    {
        if ((length & 1) != 0 || offset + length > path.Length)
        {
            name = null;
            return false;
        }

        name = Encoding.Unicode.GetString(path.Slice(offset, length));
        return true;
    }
}