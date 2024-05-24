using DiscUtils;
using DiscUtils.Streams;
using LTRData.Extensions.Formatting;
using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using Xunit;

namespace LibraryTests.Helpers;

public static class Helpers
{
    public static byte[] ReadAll(this Stream stream)
    {
        using var ms = new MemoryStream();

        stream.CopyTo(ms);

        return ms.ToArray();
    }

    public static Stream LoadTestDataFileFromGZipFile(string projectName, string dataFileName)
    {
        using var fs = File.OpenRead(Path.Combine("..", "..", "LibraryTests", projectName, "Data", dataFileName));
        using var gz = new GZipStream(fs, CompressionMode.Decompress);

        var ms = new MemoryStream();

        try
        {
            gz.CopyTo(ms);
        }
        catch (Exception)
        {
            ms.Dispose();
            throw;
        }

        ms.Seek(0, SeekOrigin.Begin);

        return ms;
    }
    public static string GetFileChecksum(string path, IFileSystem fs)
    {
#if NETCOREAPP
        var fileInfo = fs.GetFileInfo(path);
        using var file = fileInfo.OpenRead();
        Span<byte> checksum = stackalloc byte[MD5.HashSizeInBytes];
        MD5.HashData(file, checksum);
        return checksum.ToHexString();
#else
        var fileInfo = fs.GetFileInfo(path);
        using var md5 = MD5.Create();
        using var file = fileInfo.OpenRead();
        var checksum = md5.ComputeHash(file);
        return checksum.ToHexString();
#endif
    }

    public static void AssertAllZero(string path, IFileSystem fs)
    {
        var fileInfo = fs.GetFileInfo(path);
        var buffer = new byte[4 * Sizes.OneKiB];
        using var file = fileInfo.OpenRead();
        var count = file.Read(buffer, 0, buffer.Length);
        for (var i = 0; i < count; i++)
        {
            Assert.Equal(0, buffer[i]);
        }
    }

    public static string GetFileContent(string path, IFileSystem fs)
    {
        var fileInfo = fs.GetFileInfo(path);
        using var file = fileInfo.OpenText();
        return file.ReadToEnd();
    }
}
