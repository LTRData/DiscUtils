using System;
using System.Diagnostics;

namespace DiscUtils.Btrfs.Base.Items;

internal class UnknownItem(Key key) : BaseItem(key)
{
    private int size;

    public override int Size => size;

    public override int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        Trace.WriteLine($"Unsupported item type {Key.ItemType} at {PhysicalPostiiton}");

        size = buffer.Length;

        return size;
    }
}
