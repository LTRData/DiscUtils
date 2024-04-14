using System.IO;
using Xunit;

namespace LibraryTests.Utilities;

public class UtilitiesTests
{
    [Fact]
    public void CanResolveRelativePath()
    {
        var sep = Path.DirectorySeparatorChar;
        CheckResolvePath($"{sep}etc{sep}rc.d", $"init.d", $"{sep}etc{sep}init.d");
        CheckResolvePath($"{sep}etc{sep}rc.d{sep}", $"init.d", $"{sep}etc{sep}rc.d{sep}init.d");
        // For example: ({sep}TEMP{sep}Foo.txt, ..{sep}..{sep}Bar.txt) gives ({sep}Bar.txt).
        CheckResolvePath($"{sep}TEMP{sep}Foo.txt", $"..{sep}..{sep}Bar.txt", $"{sep}Bar.txt");
    }

    private static void CheckResolvePath(string basePath, string relativePath, string expectedResult)
    {
        var result = DiscUtils.Internal.Utilities.ResolvePath(basePath, relativePath);
        Assert.Equal(expectedResult, result);
    }
}
