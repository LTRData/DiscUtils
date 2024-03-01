using System;
using System.IO;
using System.IO.Compression;

namespace LibraryTests.Helpers
{
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

            MemoryStream ms = new MemoryStream();

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
    }
}
