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
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace DiscUtils.Fat;

/// <summary>
/// Free directory entry table to manage free directory entries.
/// </summary>
/// <remarks>
/// NOTE: This allocator could be optimized further to share/recycle allocations of SortedSet&lt;long&gt; instances through the <see cref="FatFileSystem"/>.
/// </remarks>
internal struct FreeDirectoryEntryTable
{
    private const int MaxBucketCount = 32;

    private uint _bucketMask;
    private readonly SortedSet<long>[] _buckets;
    private readonly Stack<SortedSet<long>> _freeList;

    /// <summary>
    /// Initializes a new instance of the <see cref="FreeDirectoryEntryTable"/> struct.
    /// </summary>
    public FreeDirectoryEntryTable()
    {
        _buckets = new SortedSet<long>[MaxBucketCount];
        _freeList = new();
    }

    /// <summary>
    /// Add a free-range of directory entries.
    /// </summary>
    /// <param name="position">The first position to be free</param>
    /// <param name="count">The number of directory entries free after the position.</param>
    public void AddFreeRange(long position, int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "The number of directory entries to add must be greater than zero.");
        }

        while (count > 0)
        {
            var bucket = Math.Min(count, MaxBucketCount);
            AddInternal(position, bucket);
            count -= bucket;
            position += bucket * DirectoryEntry.SizeOf;
        }
    }

    /// <summary>
    /// Allocate a free directory entry.
    /// </summary>
    /// <param name="originalCount">The number of directory entries to allocate.</param>
    /// <returns>The position of the first directory entry allocated or -1 if no free directory entries are available.</returns>
    public long Allocate(int originalCount)
    {
        if (originalCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(originalCount), "The number of directory entries to allocate must be greater than zero.");
        }

        if (originalCount > MaxBucketCount)
        {
            throw new ArgumentOutOfRangeException(nameof(originalCount), "The number of directory entries to allocate is too large.");
        }
        var bucketIndex = originalCount - 1;
        var bucketMask = _bucketMask >>> bucketIndex;
        // If there are no buckets or the requested count is larger than the largest bucket, return -1
        if (bucketMask == 0)
        {
            return -1;
        }

        // Find the first bucket with free space
        var offset = TrailingZeroCount(bucketMask);

        // Resolve the bucket index
        var resolvedBucketIndex = bucketIndex + offset;

        // Get the first position in the bucket
        ref var bucket = ref _buckets[resolvedBucketIndex];
        // There is a guarantee that the bucket is not null since we have a mask bit set
        var pos = bucket.Min;
        // We can remove the position from the bucket
        bucket.Remove(pos);

        // If we have a larger bucket, split it and add the remaining to the free list
        if (offset > 0)
        {
            // offset becomes the number of remaining entries that were not used by the allocation
            AddFreeRange(pos + originalCount * DirectoryEntry.SizeOf, offset);
        }

        // If the bucket is empty, remove it
        if (bucket.Count == 0)
        {
            _bucketMask &= ~(1U << resolvedBucketIndex);
            _freeList.Push(bucket);
            bucket = null;
        }

        return pos;
    }

    private void AddInternal(long position, int bucketIndex)
    {
        Debug.Assert(bucketIndex > 0);
        bucketIndex--;
        ref var bucket = ref _buckets[bucketIndex];
        bucket ??= _freeList.Count > 0 ? _freeList.Pop() : new();
        bucket.Add(position);
        _bucketMask |= 1U << bucketIndex;
    }
    
    private static int TrailingZeroCount(uint value)
    {
#if NET6_0_OR_GREATER
        return BitOperations.TrailingZeroCount(value);
#else
        // This could be more optimized but this is for old .NET framework versions
        var count = 0;
        while ((value & 1) == 0)
        {
            count++;
            value >>>= 1;
        }

        return count;
#endif
    }
}