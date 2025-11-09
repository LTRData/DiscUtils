using DiscUtils.Iso9660;
using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace LibraryTests.Iso9660;

public class DuplicateFileNamesTest
{
    [Fact]
    public void DuplicateShortNameTest()
    {
        // Test 1
        var CDBuilder = new CDBuilder();
        CDBuilder.UseJoliet = false;
        CDBuilder.AddFile(@"Folder\Filename.txt", Encoding.ASCII.GetBytes("Hello World!"));
        Assert.Throws<ArgumentException>(()
            => CDBuilder.AddFile(@"Folder\Filename.txt", Encoding.ASCII.GetBytes("Hello World!")));
    }

    [Fact]
    public void DuplicateLongNameTest()
    {
        // Test 2
        var CDBuilder = new CDBuilder();
        CDBuilder.UseJoliet = false;
        CDBuilder.AddFile(@"Folder\Extremely long filename that can't possibly fit into an ISO9660 FS.txt", Encoding.ASCII.GetBytes("Hello World!"));
        Assert.Throws<ArgumentException>(()
            => CDBuilder.AddFile(@"Folder\Extremely long filename that can't possibly fit into an ISO9660 FS.txt", Encoding.ASCII.GetBytes("Hello World! 2")));
    }

    [Fact]
    public void JolietFileNamesUnique()
    {
        var CDBuilder = new CDBuilder();
        CDBuilder.UseJoliet = true;
        CDBuilder.AddFile(@"Folder\Extremely long filename that can't possibly fit into an ISO9660 FS.txt", Encoding.ASCII.GetBytes("Hello World!"));
        CDBuilder.AddFile(@"Folder\Extremely long filename that can't possibly fit into an ISO9660 FS 2.txt", Encoding.ASCII.GetBytes("Hello World! 2"));
        CDBuilder.AddFile(@"Folder\EXTREMELY_LONG_FILENAME_THA.TXT", Encoding.ASCII.GetBytes("Hello World! 2"));

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
        CDBuilder.AddFile(@"Folder\Extremely long filename that can't possibly fit into an ISO9660 FS.txt", Encoding.ASCII.GetBytes("Hello World!"));
        CDBuilder.AddFile(@"Folder\Extremely long filename that can't possibly fit into an ISO9660 FS 2.txt", Encoding.ASCII.GetBytes("Hello World! 2"));
        CDBuilder.AddFile(@"Folder\EXTREMELY_LONG_FILENAME_THA.TXT", Encoding.ASCII.GetBytes("Hello World! 2"));

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
        CDBuilder.AddFile(@"Folder\file.txt", Encoding.ASCII.GetBytes("Hello World!"));

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
        CDBuilder.AddFile(@"Folder\file.txt", Encoding.ASCII.GetBytes("Hello World!"));

        var isoStream = new MemoryStream();
        CDBuilder.Build(isoStream);

        var cdReader = new CDReader(isoStream, joliet: false);

        Assert.Equal(Iso9660Variant.Iso9660, cdReader.ActiveVariant);
    }
}
