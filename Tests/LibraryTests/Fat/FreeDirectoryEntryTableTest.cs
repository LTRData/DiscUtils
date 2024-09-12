//
// Copyright (c) 2024, Olof Lagerkvist and contributors
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

using System;
using DiscUtils.Fat;
using Xunit;

namespace LibraryTests.Fat;

/// <summary>
/// Tests for <see cref="FreeDirectoryEntryTable"/>.
/// </summary>
public class FreeDirectoryEntryTableTest
{
    [Fact]
    public void TestSimple()
    {
        var freeTable = new FreeDirectoryEntryTable();

        // Allocation should fail, we don't have any range free
        Assert.Equal(-1, freeTable.Allocate(1));

        // Allocate 10 entries
        freeTable.AddFreeRange(0, 10);
        Assert.Equal(0, freeTable.Allocate(10));
        Assert.Equal(-1, freeTable.Allocate(1));

        // Allocate 2 x 5 entries
        freeTable.AddFreeRange(0, 10);
        Assert.Equal(0, freeTable.Allocate(5));
        Assert.Equal(5 * DirectoryEntry.SizeOf, freeTable.Allocate(5));
        Assert.Equal(-1, freeTable.Allocate(1));

        // Allocate 20 = 10 + 5 + 4 + 1 entries
        freeTable.AddFreeRange(0, 20);
        Assert.Equal(0, freeTable.Allocate(10));
        Assert.Equal(10 * DirectoryEntry.SizeOf, freeTable.Allocate(5));
        Assert.Equal(15 * DirectoryEntry.SizeOf, freeTable.Allocate(4));
        Assert.Equal(19 * DirectoryEntry.SizeOf, freeTable.Allocate(1));
        Assert.Equal(-1, freeTable.Allocate(1));

        // Add 50 free range
        // 32 + 18
        freeTable.AddFreeRange(0, 50);
        // The free table allows to allocate 32 entries, so the first entry available will be at offset 32
        // 32 + 18 => takes on entry 18
        // 32 + 8 left
        Assert.Equal(32 * DirectoryEntry.SizeOf, freeTable.Allocate(10));
        // Cannot  allocate on 8, so allocate on 32
        // 22 + 8 left
        Assert.Equal(0 * DirectoryEntry.SizeOf, freeTable.Allocate(10));
        // 12 + 8 left
        Assert.Equal(10 * DirectoryEntry.SizeOf, freeTable.Allocate(10));
        // 2 + 8 left
        Assert.Equal(20 * DirectoryEntry.SizeOf, freeTable.Allocate(10));
        // Cannot allocate 10 consecutive entries
        Assert.Equal(-1, freeTable.Allocate(10));

        // 2 entries consecutive left
        Assert.Equal(30 * DirectoryEntry.SizeOf, freeTable.Allocate(1));
        Assert.Equal(31 * DirectoryEntry.SizeOf, freeTable.Allocate(1));

        // 8 entries consecutive left
        Assert.Equal(42 * DirectoryEntry.SizeOf, freeTable.Allocate(1));
        Assert.Equal(43 * DirectoryEntry.SizeOf, freeTable.Allocate(1));
        Assert.Equal(44 * DirectoryEntry.SizeOf, freeTable.Allocate(1));
        Assert.Equal(45 * DirectoryEntry.SizeOf, freeTable.Allocate(1));
        Assert.Equal(46 * DirectoryEntry.SizeOf, freeTable.Allocate(1));
        Assert.Equal(47 * DirectoryEntry.SizeOf, freeTable.Allocate(1));
        Assert.Equal(48 * DirectoryEntry.SizeOf, freeTable.Allocate(1));
        Assert.Equal(49 * DirectoryEntry.SizeOf, freeTable.Allocate(1));

        // We should not be able to allocate from there
        Assert.Equal(-1, freeTable.Allocate(1));
    }

    [Fact]
    public void TestInvalidValues()
    {
        var freeTable = new FreeDirectoryEntryTable();

        Assert.Throws<ArgumentOutOfRangeException>(() => freeTable.AddFreeRange(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => freeTable.AddFreeRange(0, -1));

        Assert.Throws<ArgumentOutOfRangeException>(() => freeTable.Allocate(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => freeTable.Allocate(50));
    }
}