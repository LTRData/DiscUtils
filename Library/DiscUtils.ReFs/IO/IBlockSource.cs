namespace DiscUtils.ReFs.IO;

/// <summary>Random-access, read-only byte source (image file, raw device, partition slice).</summary>
internal interface IBlockSource : IDisposable
{
    /// <summary>Total readable length in bytes.</summary>
    long Length { get; }

    /// <summary>
    ///     Reads up to <paramref name="destination" />.Length bytes at <paramref name="offset" />.
    ///     Returns fewer bytes only at the end of the source.
    /// </summary>
    int Read(long offset, Span<byte> destination);
}