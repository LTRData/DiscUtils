//
// Copyright (c) 2008-2011, Kenneth Bell
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using DiscUtils.Internal;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Fat;

/// <summary>
/// Implementation for reading the name of a file in a FAT directory entry.
/// </summary>
/// <remarks>
/// http://elm-chan.org/docs/fat_e.html
/// https://en.wikipedia.org/wiki/8.3_filename
/// Long File Name support (lfn) in VFAT is described at https://www.kernel.org/doc/Documentation/filesystems/vfat.txt
/// Other documentation used: https://en.wikipedia.org/wiki/Design_of_the_FAT_file_system
/// </remarks>
internal struct FatFileName : IEquatable<FatFileName>
{
    private const byte SpaceByte = 0x20;

    const string InvalidCharsForShortName = "\"*+,./:;<=>?[\\]|";

    public static readonly FatFileName SelfEntryName = new(".");

    public static readonly FatFileName ParentEntryName = new("..");

    public static readonly FatFileName Null = new("\0\0\0\0\0\0\0\0\0\0\0\0");

    private readonly string _shortName; // null indicates a deleted / orphaned entry

    private readonly string _longName;

    private FatFileName(string shortName, string longName = null)
    {
        _shortName = shortName;
        _longName = longName;
    }

    public readonly string ShortName => _shortName;

    public readonly string LongName => _longName;

    public readonly string FullName => _longName ?? _shortName;
    
    /// <summary>
    /// Gets the number of additional directory entries used by the long file name.
    /// </summary>
    public readonly int LfnDirectoryEntryCount => _longName is not null ? (_longName.Length + 12) / 13 : 0;

    public readonly bool Equals(FatFileName other) => Equals(this, other);

    public static bool Equals(in FatFileName a, in FatFileName b)
    {
        if (a._longName != null)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(a._longName, b._longName) ||
                StringComparer.OrdinalIgnoreCase.Equals(a._longName, b._shortName))
            {
                return true;
            }
        }
        else if (b._longName != null)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(a._shortName, b._longName))
            {
                return true;
            }
        }
        else
        {
            if (StringComparer.OrdinalIgnoreCase.Compare(a._shortName, b._shortName) == 0)
            {
                return true;
            }
        }

        return false;
    }

    public readonly FatFileName ReplaceShortName(string name, FastEncodingTable encodingTable)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        var idx = 0;
        var indexOfDot = -1;

        for (var i = 0; i < name.Length; i++)
        {
            if (idx >= 11)
            {
                throw new ArgumentException($"File name too long '{name}'", nameof(name));
            }

            var c = name[i];

            if (c < 0x20)
            {
                throw new ArgumentException($"Invalid control character at index {i} in short name '{name}'", nameof(name));
            }

            if (c == '.')
            {
                if (indexOfDot >= 0)
                {
                    throw new ArgumentException($"Multiple dots at index {i} in short name '{name}'", nameof(name));
                }

                indexOfDot = idx;
            }
            else if (c == ' ')
            {
                throw new ArgumentException($"Invalid space character at index {i} in short name '{name}'", nameof(name));
            }
            else if (InvalidCharsForShortName.IndexOf(c) >= 0 || !encodingTable.TryGetCharToByteUpperCase(c, out _))
            {
                throw new ArgumentException($"Invalid character `{c}` at index {i} in short name '{name}'", nameof(name));
            }

            idx++;
        }

        if (idx == 0)
        {
            throw new ArgumentException("Empty file name", nameof(name));
        }

        if (indexOfDot > 0 && idx - indexOfDot > 4)
        {
            throw new ArgumentException($"Extension too long in short name '{name}'", nameof(name));
        }

        return new(name, _longName);
    }

    public readonly bool IsMatch(Func<string, bool> filter)
    {
        var searchName = FullName;
        if (searchName is null)
        {
            return false;
        }

        if (searchName.IndexOf('.') < 0)
        {
            searchName += '.';
        }

        return filter(searchName);
    }

    public readonly bool IsDeleted() => _shortName is null;

    public readonly bool IsEndMarker() => _shortName is not null && _shortName.Equals(Null._shortName, StringComparison.Ordinal);

    public readonly override bool Equals(object other)
        => other is FatFileName otherName && Equals(this, otherName);

    public readonly override int GetHashCode()
    {
        var displayName = FullName;
        return displayName != null ? displayName.GetHashCode() : 0;
    }

    public readonly override string ToString() => FullName;

    /// <summary>
    /// Writes this <see cref="FatFileName"/> to a buffer as proper directory entries headers for the long file name and the short file name.
    /// </summary>
    /// <param name="buffer">The buffer to receive the directory entries initialized with the long file name and the short file name.</param>
    /// <param name="encodingTable">Encoding table</param>
    public readonly unsafe void ToDirectoryEntryBytes(Span<byte> buffer, FastEncodingTable encodingTable)
    {
        if (_shortName is null) throw new InvalidOperationException("Cannot write a deleted file name");

        if (_shortName.Equals(Null._shortName, StringComparison.Ordinal))
        {
            buffer.Fill(0x00);
            return;
        }

        var lfnCount = LfnDirectoryEntryCount;
        if (buffer.Length < (lfnCount + 1) * DirectoryEntry.SizeOf)
        {
            throw new ArgumentException("Buffer is too small", nameof(buffer));
        }

        // Make sure that the directory entry is fully initialized to 0
        var offsetToSfnEntry = lfnCount * DirectoryEntry.SizeOf;
        var sfnEntry = buffer.Slice(offsetToSfnEntry, DirectoryEntry.SizeOf);
        sfnEntry.Slice(12).Fill(0);

        var finalBytes = sfnEntry.Slice(0, 11);
        finalBytes.Fill((byte)' ');

        // Initialize the buffer with the short name
        var indexOfDot = _shortName.IndexOf('.');
        int idxFinal = 0;

        var baseNameLength = indexOfDot > 0 ? indexOfDot : _shortName.Length;

        // Process the base name (at max 8 characters)
        bool hasLowerCaseBase = false;

        for (var i = 0; i < baseNameLength; i++, idxFinal++)
        {
            var c = _shortName[i];

            // We have the guarantee if the short name that if there is a lower case base name character, then all letters are lower case
            if (char.IsLetter(c) && char.IsLower(c))
            {
                hasLowerCaseBase = true;
            }

            // Make sure that we convert it back to upper case
            var isValidChar = encodingTable.TryGetCharToByteUpperCase(c, out finalBytes[idxFinal]);
            Debug.Assert(isValidChar);
        }

        // Mark the base name as lower case if needed
        if (hasLowerCaseBase)
        {
            sfnEntry[12] |= 1 << 3;
        }
        
        // Copy the extension part
        if (indexOfDot > 0)
        {
            var hasLowerCaseExt = false;
            idxFinal = 8;
            for (int i = indexOfDot + 1; i < _shortName.Length; i++, idxFinal++)
            {
                var c = _shortName[i];

                if (char.IsLetter(c) && char.IsLower(c))
                {
                    hasLowerCaseExt = true;
                }

                // Make sure that we convert it back to upper case
                var isValidChar = encodingTable.TryGetCharToByteUpperCase(c, out finalBytes[idxFinal]);
                Debug.Assert(isValidChar);
            }

            // Mark the extension as lower case if needed
            if (hasLowerCaseExt)
            {
                sfnEntry[12] |= 1 << 4;
            }
        }

        var offset = 0;
        if (_longName != null)
        {
            // Calculate the checksum for the short name
            var checksumByte = SfnChecksum(finalBytes);

            var lfnBytes = MemoryMarshal.AsBytes(_longName.AsSpan());

            var length13 = _longName.Length % 13;
            
            for (var i = lfnCount; i > 0; i--, offset += DirectoryEntry.SizeOf)
            {
                if (i == lfnCount && length13 > 0)
                {
                    // We fill all characters with 0xff (other entries will be filled with characters or initialized below)

                    var remainingLength = length13;
                    buffer.Slice(offset, DirectoryEntry.SizeOf).Fill(0xff);

                    // TODO: This code is not endian-safe. Long file names will be messed up when this code runs on big endian machines.
                    var nextLength = Math.Min(length13, 5);

                    var localOffset = offset + 1;
                    lfnBytes.Slice(26 * (i - 1), nextLength * 2).CopyTo(buffer.Slice(localOffset));
                    localOffset += nextLength * 2;
                    remainingLength -= nextLength;
                    if (remainingLength > 0)
                    {
                        localOffset = offset + 14;
                        nextLength = Math.Min(length13 - 5, 6);
                        lfnBytes.Slice(26 * (i - 1) + 10, nextLength * 2).CopyTo(buffer.Slice(localOffset));
                        localOffset += nextLength * 2;
                        remainingLength -= nextLength;
                        if (remainingLength > 0)
                        {
                            localOffset = offset + 28;
                            nextLength = Math.Min(length13 - 11, 2);
                            lfnBytes.Slice(26 * (i - 1) + 22, nextLength * 2).CopyTo(buffer.Slice(localOffset));
                            localOffset += nextLength * 2;
                        }
                        else if (localOffset == offset + 26)
                        {
                            // We need to write the null terminator on the next field name
                            localOffset = offset + 28;
                        }
                    }
                    else if (localOffset == offset + 11)
                    {
                        // We need to write the null terminator on the next field name
                        localOffset = offset + 14;
                    }

                    buffer[localOffset] = 0;
                    buffer[localOffset + 1] = 0;
                }
                else
                {
                    // TODO: This code is not endian-safe. Long file names will be messed up when this code runs on big endian machines.
                    lfnBytes.Slice(26 * (i - 1), 10).CopyTo(buffer.Slice(offset + 1));
                    lfnBytes.Slice(26 * (i - 1) + 10, 12).CopyTo(buffer.Slice(offset + 14));
                    lfnBytes.Slice(26 * (i - 1) + 22, 4).CopyTo(buffer.Slice(offset + 28));
                }

                var seq = (byte)i;
                if (i == lfnCount)
                { 
                    seq |= 0x40;
                }

                // Initialize the fields of the Directory Entry
                buffer[offset] = seq;
                buffer[offset + 11] = 0x0f;
                buffer[offset + 12] = 0;
                buffer[offset + 13] = checksumByte;
                buffer[offset + 26] = 0;
                buffer[offset + 27] = 0;
            }
        }
    }

    /// <summary>
    /// Create a new <see cref="FatFileName"/> from a long name.
    /// </summary>
    /// <param name="name">The file name to encode.</param>
    /// <param name="encodingTable">The encoding table to use.</param>
    /// <param name="shortNameExistFunction">A function that validates if short name is already used.</param>
    /// <returns>The <see cref="FatFileName"/> instance.</returns>
    public static unsafe FatFileName FromName(string name, FastEncodingTable encodingTable, Func<string, bool> shortNameExistFunction)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(name);
#else
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }
#endif

        if (name.Length > 255)
        {
            throw new IOException($"Too long name: '{name}'");
        }

        if (name.Length == 0)
        {
            throw new ArgumentException("Empty file name", nameof(name));
        }
        
        ValidateCharsFromLongName(name);

        var shortNameBytes = stackalloc byte[12];

        // Trailing . are entirely removed from the original long name
        name = name.TrimEnd('.');

        if (name.Length == 0)
        {
            throw new ArgumentException("A file name cannot contain only '.' in its name", nameof(name));
        }

        // Now we are going to process the long name to generate the short name
        var nameSpan = name.AsSpan();
        // Strip all leading and embedded spaces from the long name.
        nameSpan = nameSpan.TrimStart(' ');
        // Strip all leading periods from the long name.
        nameSpan = nameSpan.TrimStart('.');

        // Spot the last dot in the name for the extension
        var indexOfDot = nameSpan.LastIndexOf('.');
        var baseLength = indexOfDot;
        if (baseLength < 0)
        {
            baseLength = nameSpan.Length;
        }
        
        // If we have trimmed already the name, this is a lossy conversion to short name
        var lossy = nameSpan.Length != name.Length;
        var hasNonSupportedChar = lossy;
        var isBaseAllUpper = true;
        var isBaseAllLower = true;
        var shortLength = 0;
        
        // Process the base name (at max 8 characters)
        ProcessChars(nameSpan, 0, baseLength, 8, ref lossy, ref isBaseAllUpper, ref isBaseAllLower, shortNameBytes, ref shortLength, ref hasNonSupportedChar, encodingTable);

        if (shortLength == 0)
        {
            lossy = true;
        }

        // Process the extension (at max 3 characters)
        var baseNameLength = shortLength;
        var extensionStart = baseNameLength;
        
        var isExtensionAllUpper = true;
        var isExtensionAllLower = true;
        if (indexOfDot > 0)
        {
            // In case of a base name with non-supported encoding characters, we will append a hash
            if (hasNonSupportedChar && shortLength <= 2)
            {
                var hash = LfnHash(name);
                shortLength = Math.Min(shortLength, 2);
                ConvertToHex(hash, new Span<byte>(shortNameBytes + shortLength, 4));
                shortLength += 4;
                
                baseNameLength = shortLength;
                extensionStart = baseNameLength;
            }

            // 6.	Insert a dot at the end of the primary components of the basis-name iff the basis name has an extension after the last period in the name.
            shortNameBytes[shortLength++] = (byte)'.';
            var unused = false;
            ProcessChars(nameSpan, indexOfDot + 1, nameSpan.Length, 3, ref lossy, ref isExtensionAllUpper, ref isExtensionAllLower, shortNameBytes, ref shortLength, ref unused,  encodingTable);
        }

        // Initialize the temp buffer with the short name (that will be used to generate the short name and handle collisions)
        var tempBytes = stackalloc byte[12];
        var tempLength = shortLength;
        for (var i = 0; i < shortLength; i++)
        {
            tempBytes[i] = shortNameBytes[i];
        }

        // If we have mixed characters in the base name, we need to use the long name
        // If we have mixed characters in the extension, we need to use the long name
        // Mixed cases shortname without lossy means that we might not need to append the numeric tail e.g ~1
        // So differentiate this case from lossy
        var mixedCases = (!isBaseAllLower && !isBaseAllUpper) || (!isExtensionAllLower && !isExtensionAllUpper);

        // If the short name is not lossy, then we can keep the state of lower/upper case in the shortname
        // as we will use the byte 0x0c in the directory entry to store this information
        if (!lossy && !mixedCases)
        {
            if (isBaseAllLower)
            {
                for(var i = 0; i < baseNameLength; i++)
                {
                    tempBytes[i] = (byte)char.ToLowerInvariant((char)tempBytes[i]);
                }
            }

            if (isExtensionAllLower)
            {
                for (var i = extensionStart; i < shortLength; i++)
                {
                    tempBytes[i] = (byte)char.ToLowerInvariant((char)tempBytes[i]);
                }
            }
        }

        // The "~n" string can range from "~1" to "~999999".
        const int MaxTailNumber = 999999;
        var tailNumberLengthAscii = 1;
        var tailNumber = 1;
        var forceTrailingOnFirstLossy = lossy;
        var bufferChar = stackalloc char[12];
        var encoding = encodingTable.Encoding;

        while (tailNumber <= MaxTailNumber)
        {
            var encodedShortNameChar = encoding.GetChars(tempBytes, tempLength, bufferChar, 12);
            var shortName = new string(bufferChar, 0, encodedShortNameChar);

            if (forceTrailingOnFirstLossy || shortNameExistFunction(shortName))
            {
                lossy = true;
                forceTrailingOnFirstLossy = false;

                // From https://en.wikipedia.org/wiki/8.3_filename
                // If at least 4 files or folders already exist with the same extension and first 6 characters in their short names,
                // the stripped LFN is instead truncated to the first 2 letters of the basename (or 1 if the basename has only 1 letter),
                // followed by 4 hexadecimal digits derived from an undocumented hash of the filename
                if (!hasNonSupportedChar && tailNumber > 4)
                {
                    var hash = LfnHash(name);

                    // At max 2 characters
                    baseNameLength = Math.Min(baseNameLength, 2);
                    for (var i = 0; i < baseNameLength; i++)
                    {
                        tempBytes[i] = shortNameBytes[i];
                    }

                    // 4 hexadecimal digits
                    ConvertToHex(hash, new Span<byte>(tempBytes + baseNameLength, 4));
                    baseNameLength += 4;

                    hasNonSupportedChar = true;

                    // Reset numbering
                    tailNumberLengthAscii = 1;
                    tailNumber = 1;
                }

                var trailCount = 1 + tailNumberLengthAscii;
                var maxTrail = 8 - trailCount;
                if (baseNameLength > maxTrail)
                {
                    baseNameLength = maxTrail;
                }

                // Append the numeric tail ~n
                var remaining = tailNumber;
                var idx = baseNameLength + trailCount - 1;
                while (remaining > 0)
                {
                    remaining = Math.DivRem(remaining, 10, out var digit);
                    tempBytes[idx] = (byte)('0' + digit);
                    idx--;
                }
                tempBytes[idx] = (byte)'~';

                // Append the extension
                idx = baseNameLength + trailCount;
                for (var i = extensionStart; i < shortLength; i++, idx++)
                {
                    tempBytes[idx] = shortNameBytes[i];
                }
                tempLength = idx;

                // Increment the numeric tail number for the next round
                tailNumber++;
                tailNumberLengthAscii += (tailNumber % 10) == 0 ? 1 : 0;
            }
            else
            {
                return new(shortName, lossy || mixedCases ? name : null);
            }
        }

        throw new IOException($"Too many files with the same name '{name}'");
    }

    /// <summary>
    /// Create a new <see cref="FatFileName"/> from a serialized byte array of directory entries.
    /// </summary>
    /// <param name="data">The buffer to decode the filename from.</param>
    /// <param name="encodingTable">The encoding table to use to decode shortname.</param>
    /// <param name="offset">The amount of data actually processed from <paramref name="data"/> buffer.</param>
    /// <returns></returns>
    public static unsafe FatFileName FromDirectoryEntryBytes(ReadOnlySpan<byte> data, FastEncodingTable encodingTable, out int offset)
    {
        offset = 0;

        string longName = null;

        // LFN
        if (TryGetLfnDirectoryEntryCount(data, out var lfnDirectoryEntryCount) && lfnDirectoryEntryCount > 0)
        {
            // Update the number of directory entries used
            Span<char> lfn_chars = stackalloc char[13 * lfnDirectoryEntryCount];

            lfn_chars.Clear();

            var lfn_bytes = MemoryMarshal.AsBytes(lfn_chars);

            for (var i = lfnDirectoryEntryCount; i > 0 ; i--, offset += DirectoryEntry.SizeOf)
            {
                if ((data[offset] & 0x3f) == i && ((FatAttributes)data[offset + 11] & FatAttributes.LongFileNameMask) == FatAttributes.LongFileName)
                {
                    // TODO: This code is not endian-safe. Long file names will be messed up when this code runs on big endian machines.
                    data.Slice(offset + 1, 10).CopyTo(lfn_bytes.Slice(26 * (i - 1)));
                    data.Slice(offset + 14, 12).CopyTo(lfn_bytes.Slice(26 * (i - 1) + 10));
                    data.Slice(offset + 28, 4).CopyTo(lfn_bytes.Slice(26 * (i - 1) + 22));
                }
                else
                {
                    // We have an orphaned long file name entry, we need to reset to the first entry and skip it
                    offset = DirectoryEntry.SizeOf; // inform that we only processed one entry 
                    return new FatFileName(null, null);
                }
            }

            var nullpos = lfn_chars.IndexOf('\0');
            if (nullpos < 0)
            {
                nullpos = lfn_chars.Length;
            }

            longName = lfn_chars.Slice(0, nullpos).ToString();
        }

        // If we still have long file name entry, this is invalid
        if (((FatAttributes)data[offset + 11] & FatAttributes.LongFileNameMask) == FatAttributes.LongFileName)
        {
            // We have an orphaned long file name entry, we need to reset to the first entry and skip it
            offset = DirectoryEntry.SizeOf; // inform that we only processed one entry 
            return new(null, null);
        }
        
        // Check if the shortname is entirely zeroed
        bool isNull = MemoryMarshal.Cast<byte, long>(data.Slice(0, 8))[0] == 0
                      && MemoryMarshal.Cast<byte, int>(data.Slice(11 - 4, 4))[0] == 0;
        if (isNull)
        {
            offset = DirectoryEntry.SizeOf; // inform that we only processed one entry 
            return Null;
        }

        // Verifies the checksum of the long file name entries
        if (lfnDirectoryEntryCount > 0)
        {
            var checksum = SfnChecksum(data.Slice(offset, 11));
            for (int i = 0; i < lfnDirectoryEntryCount; i++)
            {
                var lfnChecksum = data[i * DirectoryEntry.SizeOf + 13];
                if (lfnChecksum != checksum)
                {
                    // We have an orphaned long file name entry, we need to reset to the first entry and skip it
                    offset = DirectoryEntry.SizeOf; // inform that we only processed one entry 
                    return new(null, null);
                }
            }
        }
        
        // Process the name part in the short name 8.3
        var tmpBuffer = stackalloc byte[12]; // 8.3 + including `.`
        int tmpLength = 0;
        for (var i = 0; i < 8; i++)
        {
            var b = data[offset + i];
            if (b == (byte)' ') // trimming space
            {
                break;
            }

            tmpBuffer[tmpLength++] = b;
        }

        int nameLength = tmpLength;
        
        // Process the extension part in the short name 8.3
        var hasExtension = false;

        for (var i = 0; i < 3; i++)
        {
            var b = data[offset + 8 + i];
            if (b == (byte)' ') // trimming space
            {
                break;
            }

            if (!hasExtension)
            {
                tmpBuffer[tmpLength++] = (byte)'.'; // add dot
                hasExtension = true;
            }

            tmpBuffer[tmpLength++] = b;
        }

        Debug.Assert(tmpLength > 0);

        string shortName = null;
        if (tmpLength == 1 && tmpBuffer[0] == '.')
        {
            Debug.Assert(longName is null);
            shortName = ".";
        }
        else if (tmpLength == 2 && tmpBuffer[0] == '.' && tmpBuffer[1] == '.')
        {
            Debug.Assert(longName is null);
            shortName = "..";
        }
        else
        {
            // The character 0xE5 is a valid character but it is used to mark a deleted file
            // So the character 0x05 is used to represent the character 0xE5
            // When reading back we need to convert it back to 0xE5
            if (tmpBuffer[0] == 0x05)
            {
                tmpBuffer[0] = 0xe5;
            }

            // Bits 3 and 4 in offset 12 of the directory entry indicate if the name and extension are lowercase
            var d0C = data[offset + 12];
            var nameIsLowercase = (d0C & (1 << 3)) != 0;
            var extIsLowercase = (d0C & (1 << 4)) != 0;

            var utf16Buffer = stackalloc char[12];
            var utf16Length = encodingTable.Encoding.GetChars(tmpBuffer, tmpLength, utf16Buffer, 12);
            Debug.Assert(utf16Length == tmpLength);

            // We apply case information recovered from the directory entry
            if (nameIsLowercase || extIsLowercase)
            {
                if (nameIsLowercase)
                {
                    for (var i = 0; i < nameLength; i++)
                    {
                        utf16Buffer[i] = char.ToLowerInvariant(utf16Buffer[i]);
                    }
                }

                if (extIsLowercase)
                {
                    for (var i = nameLength + 1; i < tmpLength; i++)
                    {
                        utf16Buffer[i] = char.ToLowerInvariant(utf16Buffer[i]);
                    }
                }
            }

            shortName = new string(utf16Buffer, 0, utf16Length);
        }

        offset += DirectoryEntry.SizeOf;
        return new(shortName, longName);
    }

    /// <summary>
    /// Create a new <see cref="FatFileName"/> from a path.
    /// </summary>
    /// <param name="parentDir">The parent directory to create the entry</param>
    /// <param name="path"></param>
    /// <param name="encodingTable"></param>
    /// <returns></returns>
    public static FatFileName NewPath(Directory parentDir, string path, FastEncodingTable encodingTable) => FromName(Utilities.GetFileFromPath(path), encodingTable, parentDir.CheckIfShortNameExists);

    /// <summary>
    /// Trie to get the number of additional directory entries used by the long file name.
    /// </summary>
    /// <param name="data">The buffer containing the first directory entry to process.</param>
    /// <param name="count">The number of additional long file name directory entries if this function returns <c>true</c>.</param>
    /// <returns><c>true</c> if the buffer contains a long file name directory entry, <c>false</c> otherwise.</returns>
    public static bool TryGetLfnDirectoryEntryCount(ReadOnlySpan<byte> data, out int count)
    {
        count = 0;

        var d0 = data[0];
        if ((d0 & 0xc0) == 0x40 && data[11] == 0x0f)
        {
            count = d0 & 0x3f;
            return true;
        }

        return false;
    }

    public static bool operator ==(FatFileName a, FatFileName b) => Equals(a, b);

    public static bool operator !=(FatFileName a, FatFileName b) => !Equals(a, b);

    private static bool Contains(byte[] array, byte val) => Array.IndexOf(array, val) >= 0;

    /// <summary>
    /// Verify that the name does not contain invalid characters.
    /// </summary>
    /// <param name="name">The name to verify.</param>
    /// <exception cref="ArgumentException">The name contains invalid characters.</exception>
    private static void ValidateCharsFromLongName(string name)
    {
        // The invalid chars are composed of the InvalidCharsForShortName minus +,;=[]
        const string invalidCharsForLongName = "\"*/:<>?\\|";

        // Check invalid chars
        var indexOfInvalidChar = name.AsSpan().IndexOfAny(invalidCharsForLongName.AsSpan());
        if (indexOfInvalidChar > 0)
        {
            throw new ArgumentException($"Invalid character in file name '{name[indexOfInvalidChar]}'", nameof(name));
        }

        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] < 0x20)
            {
                throw new ArgumentException($"Invalid character in file name '{name[i]}'", nameof(name));
            }
        }
    }

    /// <summary>
    /// Function used by <see cref="FromName"/> to process the characters of the long name and compute the short name for both the base name and the extension.
    /// </summary>
    private static unsafe void ProcessChars(ReadOnlySpan<char> name, int start, int end, int maxCount, ref bool lossy, ref bool isBaseAllUpper, ref bool isBaseAllLower, byte* shortNameBytes, ref int shortLength, ref bool hasNonSupportedChar, FastEncodingTable encodingTable)
    {
        for (int i = start; i < end; i++)
        {
            if (i - start == maxCount)
            {
                lossy = true;
                break;
            }

            var c = name[i];

            // 3.	Strip all leading and embedded spaces from the long name.
            if (c == ' ')
            {
                hasNonSupportedChar = true;
                lossy = true;
                continue;
            }

            if (c == '.')
            {
                lossy = true;
                continue;
            }

            if (char.IsLetter(c))
            {
                if (!char.IsUpper(c))
                {
                    isBaseAllUpper = false;
                }

                if (!char.IsLower(c))
                {
                    isBaseAllLower = false;
                }
            }

            if (InvalidCharsForShortName.IndexOf(c) >= 0)
            {
                lossy = true;
                shortNameBytes[shortLength] = (byte)'_';
            }
            else
            {
                if (!encodingTable.TryGetCharToByteUpperCase(c, out var b))
                {
                    hasNonSupportedChar = true;
                    lossy = true;
                    continue;
                }
                shortNameBytes[shortLength] = b;
            }

            shortLength++;
        }
    }

    /// <summary>
    /// Calculates the hash for a long file name.
    /// </summary>
    /// <param name="name">The long filename</param>
    /// <returns>The u16 hash</returns>
    private static ushort LfnHash(string name)
    {
        // Amazing work from Tom Galvin to recover this algorithm
        // http://tomgalvin.uk/blog/gen/2015/06/09/filenames/
        // https://web.archive.org/web/20230825135743/https://tomgalvin.uk/blog/gen/2015/06/09/filenames/
        ushort checksum = 0;

        for (int i = 0; i < name.Length; i++)
        {
            checksum = (ushort)(checksum * 0x25 + name[i]);
        }

        int temp = checksum * 314159269;
        if (temp < 0) temp = -temp;
        temp -= (int)(((ulong)((long)temp * 1152921497) >> 60) * 1000000007);
        checksum = (ushort)temp;

        // reverse nibble order
        checksum = (ushort)(
            ((checksum & 0xf000) >> 12) |
            ((checksum & 0x0f00) >> 4) |
            ((checksum & 0x00f0) << 4) |
            ((checksum & 0x000f) << 12));

        return checksum;
    }

    /// <summary>
    /// Calculate the checksum for a short file name.
    /// </summary>
    /// <param name="shortName">The short file name</param>
    /// <returns>The checksum</returns>
    private static byte SfnChecksum(ReadOnlySpan<byte> shortName)
    {
        byte sum = 0;
        for (var i = 0; i < shortName.Length; i++)
        {
            sum = (byte)((sum << 7) + (sum >> 1) + shortName[i]);
        }
        return sum;
    }

    private static void ConvertToHex(ushort value, Span<byte> buffer)
    {
        buffer[0] = ByteToHex((value >> 12) & 0x0f);
        buffer[1] = ByteToHex((value >> 8) & 0x0f);
        buffer[2] = ByteToHex((value >> 4) & 0x0f);
        buffer[3] = ByteToHex(value & 0x0f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ByteToHex(int b) => Unsafe.Add(ref MemoryMarshal.GetReference(ByteToHexLookup), b);

    private static ReadOnlySpan<byte> ByteToHexLookup =>
    [
        (byte)'0',
        (byte)'1',
        (byte)'2',
        (byte)'3',
        (byte)'4',
        (byte)'5',
        (byte)'6',
        (byte)'7',
        (byte)'8',
        (byte)'9',
        (byte)'A',
        (byte)'B',
        (byte)'C',
        (byte)'D',
        (byte)'E',
        (byte)'F',
    ];
}