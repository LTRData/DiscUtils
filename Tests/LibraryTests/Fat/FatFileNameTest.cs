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
using Xunit.Abstractions;

namespace LibraryTests.Fat;

public class FatFileNameTest
{

    private readonly ITestOutputHelper _output;
    public FatFileNameTest(ITestOutputHelper output)
    {
        _output = output;
    }

    // Generate short name from long name Xunit tests cases
    [Theory]
    [InlineData("A", "A", false)]
    [InlineData("A.B", "A.B", false)]
    [InlineData("a", "a", false)]
    [InlineData("a.b", "a.b", false)]
    [InlineData("a.B", "a.B", false)]
    [InlineData("A1234567", "A1234567", false)]
    [InlineData("A1234567.ext", "A1234567.ext", false)]
    [InlineData("this_is_a_long_name", "THIS_I~1", true)]
    [InlineData("V1Abcd_this_is_to_long.TXT", "V1ABCD~2.TXT", true)]
    [InlineData("V2Abcd_this_is_to_long.TXT", "V2DB58~1.TXT", true)]
    [InlineData("✨.txt", "6393~1.TXT", true)]
    [InlineData("abcdef🙂.txt", "ABCDEF~1.TXT", true)]
    [InlineData("abc🙂.txt", "ABC~1.TXT", true)]
    [InlineData("ab🙂.txt", "AB1F60~1.TXT", true)]
    [InlineData("c d.txt", "CD2C67~1.TXT", true)]
    [InlineData("...txt", "TXT~1", true)]
    [InlineData("..a.txt", "A2632~1.TXT", true)]
    [InlineData("txt...", "txt", false)]
    [InlineData("a+b=c", "A_B_C~1", true)]
    [InlineData("ab    .txt", "AB1775~1.TXT", true)]
    [InlineData("✨TAT", "TAT~1", true)]
    [InlineData("a.b..c.d", "ABC~1.D", true)]
    [InlineData("Mixed.Cas", "MIXED.CAS", true)]
    [InlineData("Mixed.txt", "MIXED.TXT", true)]
    [InlineData("mixed.Txt", "MIXED.TXT", true)]
    public void TestShortName(string name, string expectedShortName, bool expectedLongName)
    {
        Func<string, bool> resolver = name.StartsWith("V1") ? IsShortNameExits1 : name.StartsWith("V2") ? IsShortNameExists2 : shortName => false;
        var fileName = FatFileName.FromName(name, FastEncodingTable.Default, resolver);
        Assert.Equal(expectedShortName, fileName.ShortName);
        Assert.Equal(expectedLongName, fileName.LongName is not null);
        if (expectedLongName)
        {
            Assert.Equal(name, fileName.LongName);
        }

        var size = (1 + fileName.LfnDirectoryEntryCount) * DirectoryEntry.SizeOf;
        var buffer = new byte[size];
        fileName.ToDirectoryEntryBytes(buffer, FastEncodingTable.Default);

        var fileName2 = FatFileName.FromDirectoryEntryBytes(buffer, FastEncodingTable.Default, out int offset);

        Assert.Equal(size, offset);
        Assert.Equal(fileName.ShortName, fileName2.ShortName);
        Assert.Equal(fileName.LongName, fileName2.LongName);


        static bool IsShortNameExits1(string name)
        {
            return name.Equals("V1ABCD~1.TXT", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsShortNameExists2(string name)
        {
            return name.Equals("V2ABCD~1.TXT", StringComparison.OrdinalIgnoreCase)
                || name.Equals("V2ABCD~2.TXT", StringComparison.OrdinalIgnoreCase)
                || name.Equals("V2ABCD~3.TXT", StringComparison.OrdinalIgnoreCase)
                || name.Equals("V2ABCD~4.TXT", StringComparison.OrdinalIgnoreCase);
        }
    }
}