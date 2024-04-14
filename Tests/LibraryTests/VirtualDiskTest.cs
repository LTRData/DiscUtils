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

using System;
using System.IO;
using DiscUtils.Streams;
using Xunit;

namespace LibraryTests;

public class VirtualDiskTest
{
    [Fact]
    public void TestSignature()
    {
        var ms = new MemoryStream();
        ms.SetLength(1024 * 1024);

        var rawDisk = new DiscUtils.Raw.Disk(ms, Ownership.Dispose);
        Assert.Equal(0, rawDisk.Signature);
        rawDisk.Signature = unchecked((int)0xDEADBEEF);
        Assert.Equal(unchecked((int)0xDEADBEEF), rawDisk.Signature);
    }

    [Fact]
    public void TestMbr()
    {
        var ms = new MemoryStream();
        ms.SetLength(1024 * 1024);

        var newMbr = new byte[512];
        for (var i = 0; i < 512; i++)
        {
            newMbr[i] = (byte)i;
        }

        var rawDisk = new DiscUtils.Raw.Disk(ms, Ownership.Dispose);
        rawDisk.SetMasterBootRecord(newMbr);

        var readMbr = rawDisk.GetMasterBootRecord();
        Assert.Equal(512, readMbr.Length);

        for (var i = 0; i < 512; i++)
        {
            if (readMbr[i] != (byte)i)
            {
                Assert.Fail($"Mismatch on byte {i}, expected {(byte)i} was {readMbr[i]}");
            }
        }
    }

    [Fact]
    public void TestMbr_Null()
    {
        var ms = new MemoryStream();
        ms.SetLength(1024 * 1024);

        var rawDisk = new DiscUtils.Raw.Disk(ms, Ownership.Dispose);
        Assert.Throws<ArgumentException>(() => rawDisk.SetMasterBootRecord(null));
    }

    [Fact]
    public void TestMbr_WrongSize()
    {
        var ms = new MemoryStream();
        ms.SetLength(1024 * 1024);

        var rawDisk = new DiscUtils.Raw.Disk(ms, Ownership.Dispose);
        Assert.Throws<ArgumentException>(() => rawDisk.SetMasterBootRecord(new byte[513]));
    }
}
