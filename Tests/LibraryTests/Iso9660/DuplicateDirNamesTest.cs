using DiscUtils.Iso9660;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace LibraryTests.Iso9660;

public class DuplicateDirNamesTest
{
    [Fact]
    public void DuplicateShortNameTest()
    {
        // Test 1
        var CDBuilder = new CDBuilder();
        CDBuilder.UseJoliet = false;
        CDBuilder.AddDirectory(@"Folder\Filename.txt");
        CDBuilder.AddDirectory(@"Folder\Filename.txt");
    }

    [Fact]
    public void DuplicateLongNameTest()
    {
        // Test 2
        var CDBuilder = new CDBuilder();
        CDBuilder.UseJoliet = false;
        CDBuilder.AddDirectory(@"Folder\Extremely long filename that can't possibly fit into an ISO9660 FS.txt");
        CDBuilder.AddDirectory(@"Folder\Extremely long filename that can't possibly fit into an ISO9660 FS.txt");
    }

    [Fact]
    public void JolietFileNamesUnique()
    {
        var CDBuilder = new CDBuilder();
        CDBuilder.UseJoliet = true;
        CDBuilder.AddDirectory(@"Folder\Extremely long filename that can't possibly fit into an ISO9660 FS.txt");
        CDBuilder.AddDirectory(@"Folder\Extremely long filename that can't possibly fit into an ISO9660 FS 2.txt");
        CDBuilder.AddDirectory(@"Folder\EXTREMELY_LONG_FILENAME_THA.TXT");

        var isoStream = new MemoryStream();
        CDBuilder.Build(isoStream);

        using (var CDReader = new CDReader(isoStream, joliet: true))
        {
            var folder = CDReader.GetDirectoryInfo("Folder");
            var count = folder.GetFiles().Count();
            var uniqueCount = folder.GetFiles().Select(static file => file.Name).Distinct().Count();
            Assert.Equal(count, uniqueCount);
        }

        using (var CDReader = new CDReader(isoStream, joliet: false))
        {
            var folder = CDReader.GetDirectoryInfo("Folder");
            var count = folder.GetFiles().Count();
            var uniqueCount = folder.GetFiles().Select(static file => file.Name).Distinct().Count();
            Assert.Equal(count, uniqueCount);
        }
    }

    [Fact]
    public void ShortFileNamesUnique()
    {
        var CDBuilder = new CDBuilder();
        CDBuilder.UseJoliet = false;
        CDBuilder.AddDirectory(@"Folder\Extremely long filename that can't possibly fit into an ISO9660 FS.txt");
        CDBuilder.AddDirectory(@"Folder\Extremely long filename that can't possibly fit into an ISO9660 FS 2.txt");
        CDBuilder.AddDirectory(@"Folder\EXTREMELY_LONG_FILENAME_THA.TXT");

        var isoStream = new MemoryStream();
        CDBuilder.Build(isoStream);

        using (var CDReader = new CDReader(isoStream, joliet: true))
        {
            var folder = CDReader.GetDirectoryInfo("Folder");
            var count = folder.GetFiles().Count();
            var uniqueCount = folder.GetFiles().Select(static file => file.Name).Distinct().Count();
            Assert.Equal(count, uniqueCount);
        }

        using (var CDReader = new CDReader(isoStream, joliet: false))
        {
            var folder = CDReader.GetDirectoryInfo("Folder");
            var count = folder.GetFiles().Count();
            var uniqueCount = folder.GetFiles().Select(static file => file.Name).Distinct().Count();
            Assert.Equal(count, uniqueCount);
        }
    }

    [Fact]
    public void JolietVariantTest()
    {
        var CDBuilder = new CDBuilder();
        CDBuilder.UseJoliet = true;
        CDBuilder.AddDirectory(@"Folder\file.txt");

        var isoStream = new MemoryStream();
        CDBuilder.Build(isoStream);

        var cdReader = new CDReader(isoStream, joliet: true);

        Assert.Equal(Iso9660Variant.Joliet, cdReader.ActiveVariant);
    }

    [Fact]
    public void Iso9660VariantTest()
    {
        var CDBuilder = new CDBuilder();
        CDBuilder.UseJoliet = false;
        CDBuilder.AddDirectory(@"Folder\file.txt");

        var isoStream = new MemoryStream();
        CDBuilder.Build(isoStream);

        var cdReader = new CDReader(isoStream, joliet: false);

        Assert.Equal(Iso9660Variant.Iso9660, cdReader.ActiveVariant);
    }
}
