namespace DiscUtils.ReFs.Entries.Streams;

/// <summary>Read-only, seekable stream over a stream's inline (resident) bytes.</summary>
internal sealed class ResidentFileStream(byte[] data) : FileStreamBase(data.Length)
{
    protected override int ReadAt(long position, Span<byte> destination)
    {
        var n = (int)Math.Min(destination.Length, data.Length - position);
        data.AsSpan((int)position, n).CopyTo(destination);
        return n;
    }
}