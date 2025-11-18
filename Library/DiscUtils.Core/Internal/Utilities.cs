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

using DiscUtils.Archives;
using LTRData.Extensions.Buffers;
using LTRData.Extensions.Split;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DiscUtils.Internal;

public static class Utilities
{
    #region Path Manipulation

    /// <summary>
    /// Extracts the directory part of a path.
    /// </summary>
    /// <param name="path">The path to process.</param>
    /// <returns>The directory part.</returns>
    public static string GetDirectoryFromPath(string path)
        => GetDirectoryFromPath(path.AsSpan()).ToString();

    /// <summary>
    /// Extracts the directory part of a path.
    /// </summary>
    /// <param name="path">The path to process.</param>
    /// <returns>The directory part.</returns>
    public static ReadOnlySpan<char> GetDirectoryFromPath(ReadOnlySpan<char> path)
    {
        var trimmed = path.TrimEndAny(PathSeparators);

        var index = trimmed.LastIndexOfAny(PathSeparators);
        if (index < 0)
        {
            return default; // No directory, just a file name
        }

        return trimmed.Slice(0, index);
    }

    /// <summary>
    /// Extracts the file part of a path.
    /// </summary>
    /// <param name="path">The path to process.</param>
    /// <returns>The file part of the path.</returns>
    public static string GetFileFromPath(string path)
        => GetFileFromPath(path.AsSpan()).ToString();

    /// <summary>
    /// Extracts the file part of a path that optionally
    /// ends with a path separator.
    /// </summary>
    /// <param name="path">The path to process.</param>
    /// <returns>The file part of the path.</returns>
    public static ReadOnlySpan<char> GetFileFromPath(ReadOnlySpan<char> path)
    {
        var trimmed = path.TrimEndAny(PathSeparators);

        return GetFileName(trimmed);
    }

    /// <summary>
    /// Extracts the file part of a path.
    /// </summary>
    /// <param name="path">The path to process.</param>
    /// <returns>The file part of the path.</returns>
    public static ReadOnlySpan<char> GetFileName(ReadOnlySpan<char> path)
    {
        var index = path.LastIndexOfAny(PathSeparators);
        
        if (index < 0)
        {
            return path; // No directory, just a file name
        }

        return path.Slice(index + 1);
    }

    /// <summary>
    /// Combines two paths.
    /// </summary>
    /// <param name="a">The first part of the path.</param>
    /// <param name="b">The second part of the path.</param>
    /// <returns>The combined path.</returns>
    public static string CombinePaths(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || (b.Length > 0 && b[0] is '\\' or '/'))
        {
            return b;
        }

        if (string.IsNullOrWhiteSpace(b))
        {
            return a;
        }

        var buffer = new List<ReadOnlyMemory<char>>();

        if (a[0] is '\\' or '/')
        {
            buffer.Add(default);
        }

        foreach (var entry in a.AsMemory().TokenEnum('\\', '/', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = entry.Trim();

            if (trimmed.Span.Equals("..".AsSpan(), StringComparison.Ordinal)
                && buffer.Count > 0)
            {
                buffer.RemoveAt(buffer.Count - 1);
                continue;
            }

            if (trimmed.Span.Equals(".".AsSpan(), StringComparison.Ordinal))
            {
                continue;
            }

            buffer.Add(trimmed);
        }

        foreach (var entry in b.AsMemory().TokenEnum('\\', '/', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = entry.Trim();

            if (trimmed.Span.Equals("..".AsSpan(), StringComparison.Ordinal)
                && buffer.Count > 0)
            {
                buffer.RemoveAt(buffer.Count - 1);
                continue;
            }

            if (trimmed.Span.Equals(".".AsSpan(), StringComparison.Ordinal))
            {
                continue;
            }

            buffer.Add(trimmed);
        }

        return string.Join(DirectorySeparatorString, buffer);
    }

    /// <summary>
    /// Combines two paths.
    /// </summary>
    /// <param name="a">The first part of the path.</param>
    /// <param name="b">The second part of the path.</param>
    /// <returns>The combined path.</returns>
    public static string CombinePaths(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        if (a.IsWhiteSpace() || (b.Length > 0 && b[0] is '\\' or '/'))
        {
            return b.ToString();
        }

        if (b.IsWhiteSpace())
        {
            return a.ToString();
        }

        var buffer = new List<string>();

        if (a[0] is '\\' or '/')
        {
            buffer.Add("");
        }

        foreach (var entry in a.TokenEnum('\\', '/', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = entry.Trim();

            if (trimmed.Equals("..".AsSpan(), StringComparison.Ordinal)
                && buffer.Count > 0)
            {
                buffer.RemoveAt(buffer.Count - 1);
                continue;
            }

            if (trimmed.Equals(".".AsSpan(), StringComparison.Ordinal))
            {
                continue;
            }

            buffer.Add(trimmed.ToString());
        }

        foreach (var entry in b.TokenEnum('\\', '/', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = entry.Trim();

            if (trimmed.Equals("..".AsSpan(), StringComparison.Ordinal)
                && buffer.Count > 0)
            {
                buffer.RemoveAt(buffer.Count - 1);
                continue;
            }

            if (trimmed.Equals(".".AsSpan(), StringComparison.Ordinal))
            {
                continue;
            }

            buffer.Add(trimmed.ToString());
        }

        return string.Join(DirectorySeparatorString, buffer);
    }

    /// <summary>
    /// Resolves a relative path into an absolute one.
    /// </summary>
    /// <param name="basePath">The base path to resolve from.</param>
    /// <param name="relativePath">The relative path.</param>
    /// <returns>The absolute path. If no <paramref name="basePath"/> is specified
    /// then relativePath is returned as-is. If <paramref name="relativePath"/>
    /// contains more '..' characters than the base path contains levels of 
    /// directory, the resultant string be the root drive followed by the file name.
    /// If the basePath starts with '\' (no drive specified) then the returned
    /// path will also start with '\'.
    /// For example: (\TEMP\Foo.txt, ..\..\Bar.txt) gives (\Bar.txt).
    /// </returns>
    public static string ResolveRelativePath(string? basePath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return relativePath;
        }

        basePath = Path.GetDirectoryName(basePath);

        if (string.IsNullOrWhiteSpace(basePath))
        {
            return relativePath;
        }

        var merged = Path.GetFullPath(Path.Combine(basePath, relativePath));

        if (merged.Length > 2 &&
            (basePath[0] is '\\' or '/') &&
            merged[1] == ':' && merged[2] == '\\')
        {
            return merged.Substring(2);
        }

        return merged;
    }

    public static string ResolvePath(string basePath, string path)
    {
        if (path.Length == 0 || (path[0] is not '\\' and not '/'))
        {
            return ResolveRelativePath(basePath, path);
        }

        return path;
    }

    internal static readonly char[] PathSeparators = ['\\', '/'];

    public static string MakeRelativePath(string path, string basePath)
    {
        var pathElements = path.AsMemory().TokenEnum('\\', '/', StringSplitOptions.RemoveEmptyEntries).ToArray();
        var basePathElements = basePath.AsMemory().TokenEnum('\\', '/', StringSplitOptions.RemoveEmptyEntries).ToArray().AsSpan();

        if (basePathElements.Length > 0 && basePath[basePath.Length - 1] != Path.DirectorySeparatorChar)
        {
            basePathElements = basePathElements.Slice(0, basePathElements.Length - 1);
        }

        // Find first part of paths that don't match
        var i = 0;
        while (i < Math.Min(pathElements.Length - 1, basePathElements.Length))
        {
            if (!pathElements[i].Span.Equals(basePathElements[i].Span, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            ++i;
        }

        // For each remaining part of the base path, insert '..'
        var result = new StringBuilder();
        if (i == basePathElements.Length)
        {
            result.Append('.').Append(Path.DirectorySeparatorChar);
        }
        else if (i < basePathElements.Length)
        {
            for (var j = 0; j < basePathElements.Length - i; ++j)
            {
                result.Append("..").Append(Path.DirectorySeparatorChar);
            }
        }

        // For each remaining part of the path, add the path element
        for (var j = i; j < pathElements.Length - 1; ++j)
        {
            result.Append(pathElements[j]).Append(Path.DirectorySeparatorChar);
        }

        result.Append(pathElements[pathElements.Length - 1]);

        // If the target was a directory, put the terminator back
        if (path[path.Length - 1] == Path.DirectorySeparatorChar)
        {
            result.Append(Path.DirectorySeparatorChar);
        }

        return result.ToString();
    }

    #endregion

    #region Filesystem Support

    /// <summary>
    /// Indicates if a file name matches the 8.3 pattern.
    /// </summary>
    /// <param name="name">The name to test.</param>
    /// <param name="ignoreCase">If true, also accepts lowercase letters as allowed 8.3 name characters.</param>
    /// <returns><c>true</c> if the name is 8.3, otherwise <c>false</c>.</returns>
    public static bool Is8Dot3(string name, bool ignoreCase)
        => Is8Dot3(name.AsSpan(), ignoreCase);

    /// <summary>
    /// Indicates if a file name matches the 8.3 pattern.
    /// </summary>
    /// <param name="name">The name to test.</param>
    /// <param name="ignoreCase">If true, also accepts lowercase letters as allowed 8.3 name characters.</param>
    /// <returns><c>true</c> if the name is 8.3, otherwise <c>false</c>.</returns>
    public static bool Is8Dot3(ReadOnlySpan<char> name, bool ignoreCase)
    {
        if (name.Length is 0 or > 12)
        {
            return false;
        }

        if (name[0] == '.')
        {
            return false;
        }

        var i = name.LastIndexOf('.');
        
        // Check for more than one dot
        if (i >= 0 && name.Slice(0, i).LastIndexOf('.') >= 0)
        {
            return false;
        }

        var namePart = i >= 0 ? name.Slice(0, i) : name;
        var extPart = i >= 0 ? name.Slice(i + 1) : default;

        if (namePart.Length is 0 or > 8
            || extPart.Length > 3)
        {
            return false;
        }

        // Check for invalid chars
        foreach (var ch in namePart)
        {
            if (!Is8Dot3Char(ch, ignoreCase))
            {
                return false;
            }
        }

        foreach (var ch in extPart)
        {
            if (!Is8Dot3Char(ch, ignoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public static bool Is8Dot3Char(char ch, bool ignoreCase)
        => (ch is >= 'A' and <= 'Z')
        || (ch is >= '0' and <= '9')
        || (ignoreCase && ch is >= 'a' and <= 'z')
        || "_^$~!#%£-{}()@'`&".Contains(ch);

    private static readonly ConcurrentDictionary<(string pattern, bool ignoreCase), Func<string, bool>> wildcardsCache = new();

    /// <summary>
    /// Converts a 'standard' wildcard file/path specification into a regular expression.
    /// </summary>
    /// <param name="pattern">The wildcard pattern to convert.</param>
    /// <param name="ignoreCase"></param>
    /// <returns>The resultant regular expression.</returns>
    /// <remarks>
    /// The wildcard * (star) matches zero or more characters (including '.'), and ?
    /// (question mark) matches precisely one character (except '.').
    /// </remarks>
    public static Func<string, bool>? ConvertWildcardsToRegEx(string? pattern, bool ignoreCase)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(pattern);
#else
        if (pattern is null)
        {
            throw new ArgumentNullException(nameof(pattern));
        }
#endif

        if (pattern is "*" or "*.*")
        {
            return null;
        }

        static Func<string, bool> filterFactory((string pattern, bool ignoreCase) key)
        {
            if (key.pattern.AsSpan().IndexOfAny('*', '?') < 0)
            {
                if (!key.pattern.Contains('.'))
                {
                    key.pattern += '.';
                }

                if (key.ignoreCase)
                {
                    return name => StringComparer.OrdinalIgnoreCase.Equals(name, key.pattern);
                }
                else
                {
                    return key.pattern.Equals;
                }
            }

            if (!key.pattern.Contains('.'))
            {
                key.pattern += ".*";
            }

            var regexOptions = RegexOptions.CultureInvariant | RegexOptions.Compiled;

            if (key.ignoreCase)
            {
                regexOptions |= RegexOptions.IgnoreCase;
            }

            var query = $"^{Regex.Escape(key.pattern).Replace(@"\*", ".*").Replace(@"\?", "[^.]")}$";

            return new Regex(query, regexOptions).IsMatch;
        }

        return wildcardsCache.GetOrAdd((pattern, ignoreCase), filterFactory);
    }

    public static FileAttributes FileAttributesFromUnixFileType(this UnixFileType fileType)
    {
        return fileType switch
        {
            UnixFileType.Regular => FileAttributes.Normal,
            UnixFileType.Directory => FileAttributes.Directory,
            UnixFileType.Link => FileAttributes.ReparsePoint,
            UnixFileType.Fifo or UnixFileType.Character or UnixFileType.Block or UnixFileType.Socket => FileAttributes.Device | FileAttributes.System,
            _ => 0,
        };
    }

    public static FileAttributes FileAttributesFromUnixFilePermissions(string name, UnixFilePermissions fileMode, UnixFileType fileType)
    {
        var attr = fileType.FileAttributesFromUnixFileType();

        if (!fileMode.HasFlag(UnixFilePermissions.OwnerWrite))
        {
            attr |= FileAttributes.ReadOnly;
        }

        if (GetFileFromPath(name.AsSpan()).StartsWith(".".AsSpan(), StringComparison.Ordinal))
        {
            attr |= FileAttributes.Hidden;
        }

        return attr;
    }

    public static UnixFilePermissions UnixFilePermissionsFromFileAttributes(this FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReadOnly) == 0)
        {
            return UnixFilePermissions.OwnerAll | UnixFilePermissions.GroupAll | UnixFilePermissions.OthersAll;
        }

        return UnixFilePermissions.OwnerRead | UnixFilePermissions.GroupRead | UnixFilePermissions.OthersRead |
            UnixFilePermissions.OwnerExecute | UnixFilePermissions.GroupExecute | UnixFilePermissions.OthersExecute;
    }

    public static string DirectorySeparatorString { get; } = Path.DirectorySeparatorChar.ToString();

    public static bool StartsWithDirectorySeparator(this string path) =>
        path is not null && path.Length > 0 && (path[0] is '/' or '\\');

    public static bool EndsWithDirectorySeparator(this string path) =>
        path is not null && path.Length > 0 && (path[path.Length - 1] is '/' or '\\');

    public static UnixFileType ToUnixFileType(this FileAttributes attributes)
    {
        if (attributes.HasFlag(FileAttributes.Directory))
        {
            return UnixFileType.Directory;
        }
        else if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return UnixFileType.Link;
        }
        else if (attributes.HasFlag(FileAttributes.Device))
        {
            // Could be block, char, fifo, socket - no way to distinguish
            return UnixFileType.Block;
        }
        else
        {
            return UnixFileType.Regular;
        }
    }

    public static UnixFileType ToUnixFileType(this TarFileType entryType)
    {
        return entryType switch
        {
            TarFileType.TarEntryRegularFile or TarFileType.TarEntryContiguous => UnixFileType.Regular,
            TarFileType.TarEntryDirectory => UnixFileType.Directory,
            TarFileType.TarEntryLink or TarFileType.TarEntrySymbolicLink or TarFileType.TarEntryLongLinkTarget => UnixFileType.Link,
            TarFileType.TarEntryCharacter => UnixFileType.Character,
            TarFileType.TarEntryBlock => UnixFileType.Block,
            TarFileType.TarEntryFifo => UnixFileType.Fifo,
            _ => UnixFileType.None,
        };
    }

    #endregion

#if NETFRAMEWORK && !NET462_OR_GREATER
    public static ImmutableArray<T> ToImmutableArray<T>(this ReadOnlySpan<T> source)
        => ImmutableArray.Create(source.ToArray());

    public static ImmutableArray<T> ToImmutableArray<T>(this Span<T> source)
        => ImmutableArray.Create(source.ToArray());
#endif
}
