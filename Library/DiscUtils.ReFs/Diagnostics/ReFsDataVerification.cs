using DiscUtils.ReFs.Diagnostics.Enums;

namespace DiscUtils.ReFs.Diagnostics;

/// <summary>
/// The result of <see cref="ReFsFileSystem.VerifyData"/>: bytes read from a file's streams, how many were checked against checksums and with
/// which kinds, and how many were never written (they read as zeros and have nothing to check).
/// </summary>
/// <param name="Bytes">Bytes read.</param>
/// <param name="VerifiedBytes">Bytes checked against a checksum.</param>
/// <param name="UnwrittenBytes">Bytes never written.</param>
/// <param name="Kinds">The checksum kinds that were checked.</param>
public sealed record ReFsDataVerification(ulong Bytes, ulong VerifiedBytes, ulong UnwrittenBytes, IReadOnlyList<ReFsChecksumKind> Kinds)
{
    /// <summary>True when any of the data was checked against a checksum.</summary>
    public bool HasChecksums => VerifiedBytes > 0;

    /// <summary>Bytes that hold written data.</summary>
    public ulong WrittenBytes => Bytes - Math.Min(Bytes, UnwrittenBytes);

    /// <summary>Written bytes that had no checksum to check.</summary>
    public ulong UncheckedBytes => WrittenBytes - Math.Min(WrittenBytes, VerifiedBytes);

    /// <summary>True when every written byte was checked (a file without written data has nothing to check and is not).</summary>
    public bool FullyVerified => WrittenBytes > 0 && UncheckedBytes == 0;
}