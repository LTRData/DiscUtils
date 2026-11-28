using DiscUtils.Streams;

namespace DiscUtils.ReFs.IO;

/// <summary>Read-only random access over a seekable stream that starts at the ReFS volume. The stream is not owned.</summary>
internal sealed class StreamBlockSource(Stream stream) : IBlockSource
{
    public long Length { get; } = stream.Length;

    public int Read(long offset, Span<byte> destination)
    {
        if (offset < 0 || offset > Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        stream.Position = offset;
        var total = stream.ReadMaximum(destination.Length > Length - offset ? destination.Slice(0, (int)(Length - offset)) : destination);

        return total;
    }

    public void Dispose()
    {
        // The stream belongs to the caller.
    }
}