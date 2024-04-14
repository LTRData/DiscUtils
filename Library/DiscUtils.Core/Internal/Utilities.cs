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

using LTRData.Extensions.Buffers;
using LTRData.Extensions.Split;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DiscUtils.Internal;

public static class Utilities
{
    /// <summary>
    /// Indicates if two ranges overlap.
    /// </summary>
    /// <typeparam name="T">The type of the ordinals.</typeparam>
    /// <param name="xFirst">The lowest ordinal of the first range (inclusive).</param>
    /// <param name="xLast">The highest ordinal of the first range (exclusive).</param>
    /// <param name="yFirst">The lowest ordinal of the second range (inclusive).</param>
    /// <param name="yLast">The highest ordinal of the second range (exclusive).</param>
    /// <returns><c>true</c> if the ranges overlap, else <c>false</c>.</returns>
    public static bool RangesOverlap<T>(T xFirst, T xLast, T yFirst, T yLast) where T : IComparable<T>
    {
        return !((xLast.CompareTo(yFirst) <= 0) || (xFirst.CompareTo(yLast) >= 0));
    }

    #region Bit Twiddling

    public static bool IsAllZeros(byte[] buffer, int offset, int count)
    {
        var end = offset + count;
        for (var i = offset; i < end; ++i)
        {
            if (buffer[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsAllZeros(ReadOnlySpan<byte> buffer)
    {
        for (var i = 0; i < buffer.Length; ++i)
        {
            if (buffer[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsPowerOfTwo(uint val)
    {
        if (val == 0)
        {
            return false;
        }

        while ((val & 1) != 1)
        {
            val >>= 1;
        }

        return val == 1;
    }

    public static bool IsPowerOfTwo(long val)
    {
        if (val == 0)
        {
            return false;
        }

        while ((val & 1) != 1)
        {
            val >>= 1;
        }

        return val == 1;
    }

    public static bool AreEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        if (ReferenceEquals(a, b))
        {
            return true;
        }

        for (var i = 0; i < a.Length; ++i)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    public static ushort BitSwap(ushort value)
    {
        return (ushort)(((value & 0x00FF) << 8) | ((value & 0xFF00) >> 8));
    }

    public static uint BitSwap(uint value)
    {
        return ((value & 0xFF) << 24) | ((value & 0xFF00) << 8) | ((value & 0x00FF0000) >> 8) |
               ((value & 0xFF000000) >> 24);
    }

    public static ulong BitSwap(ulong value)
    {
        return ((ulong)BitSwap((uint)(value & 0xFFFFFFFF)) << 32) | BitSwap((uint)(value >> 32));
    }

    public static short BitSwap(short value)
    {
        return (short)BitSwap((ushort)value);
    }

    public static int BitSwap(int value)
    {
        return (int)BitSwap((uint)value);
    }

    public static long BitSwap(long value)
    {
        return (long)BitSwap((ulong)value);
    }

    #endregion

    #region Path Manipulation

    /// <summary>
    /// Extracts the directory part of a path.
    /// </summary>
    /// <param name="path">The path to process.</param>
    /// <returns>The directory part.</returns>
    public static string GetDirectoryFromPath(string path)
    {
        var trimmed = path.AsSpan().TrimEndAny(PathSeparators);

        var index = trimmed.LastIndexOfAny(PathSeparators);
        if (index < 0)
        {
            return string.Empty; // No directory, just a file name
        }

        return trimmed.Slice(0, index).ToString();
    }

    /// <summary>
    /// Extracts the file part of a path.
    /// </summary>
    /// <param name="path">The path to process.</param>
    /// <returns>The file part of the path.</returns>
    public static string GetFileFromPath(string path)
    {
        var trimmed = path.AsSpan().TrimEndAny(PathSeparators);

        var index = trimmed.LastIndexOfAny(PathSeparators);
        if (index < 0)
        {
            return trimmed.ToString(); // No directory, just a file name
        }

        return trimmed.Slice(index + 1).ToString();
    }

    /// <summary>
    /// Combines two paths.
    /// </summary>
    /// <param name="a">The first part of the path.</param>
    /// <param name="b">The second part of the path.</param>
    /// <returns>The combined path.</returns>
    public static string CombinePaths(string a, string b)
    {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
        return Path.Combine(a, b);
#else
        if (string.IsNullOrEmpty(a) || (b.Length > 0 && b[0] is '\\' or '/'))
        {
            return b;
        }

        if (string.IsNullOrEmpty(b))
        {
            return a;
        }

        return a.TrimEnd(PathSeparators) + Path.DirectorySeparatorChar + b.TrimStart(PathSeparators);
#endif
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
    public static string ResolveRelativePath(string basePath, string relativePath)
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
            (basePath[0] == '\\' || basePath[0] == '/') &&
            merged[1] == ':' && merged[2] == '\\')
        {
            return merged.Substring(2);
        }

        return merged;
    }

    public static string ResolvePath(string basePath, string path)
    {
        if (path.Length == 0 || (path[0] != '\\' && path[0] != '/'))
        {
            return ResolveRelativePath(basePath, path);
        }

        return path;
    }

    internal static readonly char[] PathSeparators = { '\\', '/' };

    public static string MakeRelativePath(string path, string basePath)
    {
        var pathElements = path.AsMemory().Split('\\', '/', StringSplitOptions.RemoveEmptyEntries).ToArray();
        var basePathElements = basePath.AsMemory().Split('\\', '/', StringSplitOptions.RemoveEmptyEntries).ToArray();

        if (basePathElements.Length > 0 && basePath[basePath.Length - 1] != Path.DirectorySeparatorChar)
        {
            Array.Resize(ref basePathElements, basePathElements.Length - 1);
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
    /// <returns><c>true</c> if the name is 8.3, otherwise <c>false</c>.</returns>
    public static bool Is8Dot3(string name)
    {
        if (name.Length > 12)
        {
            return false;
        }

        var split = name.Split('.');

        if (split.Length is > 2 or < 1)
        {
            return false;
        }

        if (split[0].Length > 8)
        {
            return false;
        }

        foreach (var ch in split[0])
        {
            if (!Is8Dot3Char(ch))
            {
                return false;
            }
        }

        if (split.Length > 1)
        {
            if (split[1].Length > 3)
            {
                return false;
            }

            foreach (var ch in split[1])
            {
                if (!Is8Dot3Char(ch))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static bool Is8Dot3Char(char ch)
    {
        return (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || "_^$~!#%£-{}()@'`&".Contains(ch);
    }

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
    public static Func<string, bool> ConvertWildcardsToRegEx(string pattern, bool ignoreCase)
    {
        if (pattern is "*" or "*.*")
        {
            return null;
        }

        if (!pattern.Contains('.'))
        {
            pattern += ".";
        }

        if (pattern.AsSpan().IndexOfAny('*', '?') < 0)
        {
            if (ignoreCase)
            {
                return name => StringComparer.OrdinalIgnoreCase.Equals(name, pattern);
            }
            else
            {
                return name => StringComparer.Ordinal.Equals(name, pattern);
            }
        }

        var regexOptions = RegexOptions.CultureInvariant;
        if (ignoreCase)
        {
            regexOptions |= RegexOptions.IgnoreCase;
        }

        var query = $"^{Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", "[^.]")}$";
        return new Regex(query, regexOptions).IsMatch;
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

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
        if (Path.GetFileName(name.AsSpan()).StartsWith(".", StringComparison.Ordinal))
        {
            attr |= FileAttributes.Hidden;
        }
#else
        if (Path.GetFileName(name).StartsWith(".", StringComparison.Ordinal))
        {
            attr |= FileAttributes.Hidden;
        }
#endif

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
        path is not null && path.Length > 0 && (path[0] == '/' || path[0] == '\\');

    public static bool EndsWithDirectorySeparator(this string path) =>
        path is not null && path.Length > 0 && (path[path.Length - 1] == '/' || path[path.Length - 1] == '\\');

#endregion
}
