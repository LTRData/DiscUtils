namespace DiscUtils.ReFs;

/// <summary>
///     The file is EFS-encrypted (attribute <see cref="Entries.Enums.ReFsAttributes.Encrypted" />, or a run with <c>EntryProperties.Encrypted</c>): its data on
///     disk is ciphertext and this reader does not decrypt it, so it returns none rather than ciphertext presented as file data.
/// </summary>
internal sealed class ReFsEncryptedDataException(string message) : NotSupportedException(message);
