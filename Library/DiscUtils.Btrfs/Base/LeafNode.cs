//
// Copyright (c) 2017, Bianco Veigel
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
using System.Collections.Generic;
using System.IO;
using DiscUtils.Btrfs.Base.Items;
using DiscUtils.Streams;

namespace DiscUtils.Btrfs.Base;

internal class LeafNode:NodeHeader
{
    /// <summary>
    /// key pointers
    /// </summary>
    public NodeItem[] Items { get; private set; }

    public BaseItem[] NodeData { get; private set; }

    public override int Size => (int)(base.Size + ItemCount * KeyPointer.Length);

    public override int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        var itemOffset = base.ReadFrom(buffer);
        Items = new NodeItem[ItemCount];
        NodeData = new BaseItem[ItemCount];
        for (var i = 0; i < ItemCount; i++)
        {
            Items[i] = new NodeItem();
            itemOffset += Items[i].ReadFrom(buffer.Slice(itemOffset));
            switch (Items[i].Key.ObjectId)
            {
                case (ulong)ReservedObjectId.CsumItem:
                case (ulong)ReservedObjectId.TreeReloc:
                    continue;
                default:
                    NodeData[i] = CreateItem(Items[i], buffer.Slice(Length));
                    break;
            }
        }

        return Size;
    }

    private static BaseItem CreateItem(NodeItem item, ReadOnlySpan<byte> buffer)
    {
        var data = EndianUtilities.ToByteArray(buffer.Slice((int)(item.DataOffset), (int)item.DataSize));
        BaseItem result = item.Key.ItemType switch
        {
            ItemType.ChunkItem => new ChunkItem(item.Key),
            ItemType.DevItem => new DevItem(item.Key),
            ItemType.RootItem => new RootItem(item.Key),
            ItemType.InodeRef => new InodeRef(item.Key),
            ItemType.InodeItem => new InodeItem(item.Key),
            ItemType.DirItem => new DirItem(item.Key),
            ItemType.DirIndex => new DirIndex(item.Key),
            ItemType.ExtentData => new ExtentData(item.Key),
            ItemType.RootRef => new RootRef(item.Key),
            ItemType.RootBackref => new RootBackref(item.Key),
            ItemType.XattrItem => new XattrItem(item.Key),
            ItemType.OrphanItem => new OrphanItem(item.Key),
            _ => throw new IOException($"Unsupported item type {item.Key.ItemType}"),
        };
        result.ReadFrom(data);
        return result;
    }

    public override IEnumerable<BaseItem> Find(Key key, Context context)
    {
        for (var i = 0; i < Items.Length; i++)
        {
            if (Items[i].Key.ObjectId > key.ObjectId)
            {
                break;
            }

            if (Items[i].Key.ObjectId == key.ObjectId && Items[i].Key.ItemType == key.ItemType)
            {
                yield return NodeData[i];
            }
        }
    }
}
