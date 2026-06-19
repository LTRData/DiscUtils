using System;
using System.Text;

namespace DiscUtils.Core.WindowsSecurity.AccessControl;

public abstract class KnownAce : GenericAce
{
    public int AccessMask { get; set; }

    public SecurityIdentifier? SecurityIdentifier { get; set; }

    internal KnownAce(AceType type, AceFlags flags)
        : base(type, flags) { }

    internal KnownAce(ReadOnlySpan<byte> binaryForm)
        : base(binaryForm) { }

    internal static string GetSddlAccessRights(int accessMask)
    {
        var ret = GetSddlAliasRights(accessMask);
        return ret is null || string.IsNullOrWhiteSpace(ret)
            ? $"0x{accessMask:x}"
            : ret;
    }

    private static string? GetSddlAliasRights(int accessMask)
    {
        var rights = SddlAccessRight.Decompose(accessMask);
        if (rights == null)
        {
            return null;
        }

        var ret = new StringBuilder();
        foreach (var right in rights)
        {
            ret.Append(right.Name);
        }

        return ret.ToString();
    }
}