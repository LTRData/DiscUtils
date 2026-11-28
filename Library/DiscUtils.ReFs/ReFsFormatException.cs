namespace DiscUtils.ReFs;

/// <summary>The data is not a valid or supported ReFS structure.</summary>
internal class ReFsFormatException : InvalidFileSystemException
{
    public ReFsFormatException(string message) : base(message) { }

    public ReFsFormatException(string message, Exception inner) : base(message, inner) { }
}
