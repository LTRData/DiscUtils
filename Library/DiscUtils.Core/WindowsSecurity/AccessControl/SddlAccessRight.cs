using System;
using System.Collections.Generic;

namespace DiscUtils.Core.WindowsSecurity.AccessControl;

internal class SddlAccessRight
{
    public string Name { get; set; }
    public int Value { get; set; }
    public int ObjectType { get; set; }

    public static SddlAccessRight LookupByName(ReadOnlySpan<char> s)
    {
        foreach (var right in rights)
        {
            if (right.Name.AsSpan().Equals(s, StringComparison.CurrentCultureIgnoreCase))
            {
                return right;
            }
        }

        return null;
    }

    public static SddlAccessRight[] Decompose(int mask)
    {
        foreach (var right in rights)
        {
            if (mask == right.Value)
            {
                return [right];
            }
        }

        var foundType = 0;
        var found = new List<SddlAccessRight>();
        var accountedBits = 0;
        foreach (var right in rights)
        {
            if ((mask & right.Value) == right.Value
                && (accountedBits | right.Value) != accountedBits)
            {
                if (foundType == 0)
                {
                    foundType = right.ObjectType;
                }

                if (right.ObjectType != 0
                    && foundType != right.ObjectType)
                {
                    return null;
                }

                found.Add(right);
                accountedBits |= right.Value;
            }

            if (accountedBits == mask)
            {
                return found.ToArray();
            }
        }

        return null;
    }

    private static readonly SddlAccessRight[] rights =
    [
        new() { Name = "CC", Value = 0x00000001, ObjectType = 1 },
        new() { Name = "DC", Value = 0x00000002, ObjectType = 1 },
        new() { Name = "LC", Value = 0x00000004, ObjectType = 1 },
        new() { Name = "SW", Value = 0x00000008, ObjectType = 1 },
        new() { Name = "RP", Value = 0x00000010, ObjectType = 1 },
        new() { Name = "WP", Value = 0x00000020, ObjectType = 1 },
        new() { Name = "DT", Value = 0x00000040, ObjectType = 1 },
        new() { Name = "LO", Value = 0x00000080, ObjectType = 1 },
        new() { Name = "CR", Value = 0x00000100, ObjectType = 1 },

        new() { Name = "SD", Value = 0x00010000 },
        new() { Name = "RC", Value = 0x00020000 },
        new() { Name = "WD", Value = 0x00040000 },
        new() { Name = "WO", Value = 0x00080000 },

        new() { Name = "GA", Value = 0x10000000 },
        new() { Name = "GX", Value = 0x20000000 },
        new() { Name = "GW", Value = 0x40000000 },
        new() { Name = "GR", Value = unchecked((int)0x80000000) },

        new() { Name = "FA", Value = 0x001F01FF, ObjectType = 2 },
        new() { Name = "FR", Value = 0x00120089, ObjectType = 2 },
        new() { Name = "FW", Value = 0x00120116, ObjectType = 2 },
        new() { Name = "FX", Value = 0x001200A0, ObjectType = 2 },

        new() { Name = "KA", Value = 0x000F003F, ObjectType = 3 },
        new() { Name = "KR", Value = 0x00020019, ObjectType = 3 },
        new() { Name = "KW", Value = 0x00020006, ObjectType = 3 },
        new() { Name = "KX", Value = 0x00020019, ObjectType = 3 },

        new() { Name = "NW", Value = 0x00000001 },
        new() { Name = "NR", Value = 0x00000002 },
        new() { Name = "NX", Value = 0x00000004 },
    ];
}