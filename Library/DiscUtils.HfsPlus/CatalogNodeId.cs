//
// Copyright (c) 2008-2011, Kenneth Bell
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

using System.Globalization;

namespace DiscUtils.HfsPlus;

internal readonly struct CatalogNodeId
{
    public static readonly CatalogNodeId RootParentId = new(1);
    public static readonly CatalogNodeId RootFolderId = new(2);
    public static readonly CatalogNodeId ExtentsFileId = new(3);
    public static readonly CatalogNodeId CatalogFileId = new(4);
    public static readonly CatalogNodeId BadBlockFileId = new(5);
    public static readonly CatalogNodeId AllocationFileId = new(6);
    public static readonly CatalogNodeId StartupFileId = new(7);
    public static readonly CatalogNodeId AttributesFileId = new(8);
    public static readonly CatalogNodeId RepairCatalogFileId = new(14);
    public static readonly CatalogNodeId BogusExtentFileId = new(15);
    public static readonly CatalogNodeId FirstUserCatalogNodeId = new(16);

    private readonly uint _id;

    public CatalogNodeId(uint id)
    {
        _id = id;
    }

    public static implicit operator uint(CatalogNodeId nodeId)
    {
        return nodeId._id;
    }

    public static implicit operator CatalogNodeId(uint id)
    {
        return new CatalogNodeId(id);
    }

    public override string ToString()
    {
        return _id.ToString(CultureInfo.InvariantCulture);
    }
}