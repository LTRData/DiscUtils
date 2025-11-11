//
// Copyright (c) 2008-2012, Kenneth Bell
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//

using System;

namespace DiscUtils.Vhdx;

internal sealed class MetadataEntryKey : IEquatable<MetadataEntryKey>
{
    public MetadataEntryKey(Guid itemId, bool isUser)
    {
        ItemId = itemId;
        IsUser = isUser;
    }

    public bool IsUser { get; }

    public Guid ItemId { get; }

    public bool Equals(MetadataEntryKey other)
    {
        if (other == null)
        {
            return false;
        }

        return ItemId == other.ItemId && IsUser == other.IsUser;
    }

    public static bool operator ==(MetadataEntryKey x, MetadataEntryKey y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (((object)x == null) || ((object)y == null))
        {
            return false;
        }

        return x.ItemId == y.ItemId && x.IsUser == y.IsUser;
    }

    public static bool operator !=(MetadataEntryKey x, MetadataEntryKey y) => !(x == y);

    public static MetadataEntryKey FromEntry(MetadataEntry entry)
    {
        return new MetadataEntryKey(entry.ItemId, (entry.Flags & MetadataEntryFlags.IsUser) != 0);
    }

    public override bool Equals(object other)
    {
        var otherKey = other as MetadataEntryKey;
        if (otherKey != null)
        {
            return Equals(otherKey);
        }

        return false;
    }

    public override int GetHashCode()
    {
        return ItemId.GetHashCode() ^ (IsUser ? 0x3C13A5 : 0);
    }

    public override string ToString()
    {
        return $"{ItemId}{(IsUser ? " - User" : " - System")}";
    }
}