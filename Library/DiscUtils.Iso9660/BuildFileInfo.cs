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
using System.Globalization;
using System.IO;
using System.Text;
using DiscUtils.Internal;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Iso9660;

/// <summary>
/// Represents a file that will be built into the ISO image.
/// </summary>
public sealed class BuildFileInfo : BuildDirectoryMember, IEquatable<BuildFileInfo>
{
    private readonly byte[] _contentData;
    private readonly string _contentPath;
    private readonly Stream _contentStream;

    internal BuildFileInfo(ReadOnlyMemory<char> name, BuildDirectoryInfo parent, byte[] content)
        : base(IsoUtilities.NormalizeFileName(name.Span), MakeShortFileName(name, parent))
    {
        if (content.LongLength >= (4L << 30))
        {
            throw new InvalidOperationException("ISO 9660 file system does not support files larger than 4 GB");
        }

        Parent = parent;
        _contentData = content;
    }

    internal BuildFileInfo(ReadOnlyMemory<char> name, BuildDirectoryInfo parent, string content)
        : base(IsoUtilities.NormalizeFileName(name.Span), MakeShortFileName(name, parent))
    {
        if (new FileInfo(content).Length >= (4L << 30))
        {
            throw new InvalidOperationException("ISO 9660 file system does not support files larger than 4 GB");
        }

        Parent = parent;
        _contentPath = content;

        CreationTime = new FileInfo(_contentPath).LastWriteTimeUtc;
    }

    internal BuildFileInfo(ReadOnlyMemory<char> name, BuildDirectoryInfo parent, Stream source)
        : base(IsoUtilities.NormalizeFileName(name.Span), MakeShortFileName(name, parent))
    {
        if (source.Length >= (4L << 30))
        {
            throw new InvalidOperationException("ISO 9660 file system does not support files larger than 4 GB");
        }

        Parent = parent;
        _contentStream = source;
    }

    /// <summary>
    /// The parent directory, or <c>null</c> if none.
    /// </summary>
    public override BuildDirectoryInfo Parent { get; }

    internal override long GetDataSize(Encoding enc)
    {
        if (_contentData != null)
        {
            return _contentData.Length;
        }

        if (_contentPath != null)
        {
            return new FileInfo(_contentPath).Length;
        }

        return _contentStream.Length;
    }

    internal Stream OpenStream()
    {
        if (_contentData != null)
        {
            return new MemoryStream(_contentData, writable: false);
        }

        if (_contentPath != null)
        {
            var locator = new LocalFileLocator(string.Empty, useAsync: false);
            return locator.Open(_contentPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        return _contentStream;
    }

    internal void CloseStream(Stream s)
    {
        // Close and dispose the stream, unless it's one we were given to stream in
        // from (we might need it again).
        if (_contentStream != s)
        {
            s.Dispose();
        }
    }

    private static string MakeShortFileName(ReadOnlyMemory<char> longName, BuildDirectoryInfo parent)
    {
        if (longName.Length <= 30
            && IsoUtilities.IsValidFileName(longName.Span)
            && !parent.TryGetMemberByShortName(longName, out _))
        {
            return longName.ToString();
        }

        Span<char> shortNameChars = stackalloc char[longName.Length];
        longName.Span.ToUpperInvariant(shortNameChars);

        for (var i = 0; i < shortNameChars.Length; ++i)
        {
            if (!IsoUtilities.IsValidDChar(shortNameChars[i]) && shortNameChars[i] != '.' && shortNameChars[i] != ';')
            {
                shortNameChars[i] = '_';
            }
        }

        var (name, extension, version) = IsoUtilities.SplitFileName(shortNameChars);

        if (name.Length + extension.Length > 30)
        {
            extension = extension[..Math.Min(extension.Length, 3)];
        }

        if (name.Length + extension.Length > 30)
        {
            name = name[..(30 - extension.Length)];
        }

        for (var attempt = 0; attempt < int.MaxValue; attempt++)
        {
            if (attempt > 0)
            {
                var attemptStr = attempt.ToString(CultureInfo.InvariantCulture);

                if (name.Length + attemptStr.Length >= 30)
                {
                    name = name[..(name.Length - attemptStr.Length - 1)];
                }

#if NET6_0_OR_GREATER
                name = $"{name}_{attemptStr}";
#else
                name = $"{name.ToString()}_{attemptStr}";
#endif
            }

#if NET6_0_OR_GREATER
            var candidate = $"{name}.{extension};{version}";
#else
            var candidate = $"{name.ToString()}.{extension.ToString()};{version.ToString()}";
#endif

            if (!parent.TryGetMemberByShortName(candidate.AsMemory(), out _))
            {
                return candidate;
            }
        }

        throw new ArgumentException($"Unable to construct a unique short name for '{longName}' in directory {parent.Name}");
    }

    internal bool Equals(Stream stream) =>
        _contentStream != null &&
        ReferenceEquals(_contentStream, stream);

    internal bool Equals(byte[] data)
    {
        if (_contentData == null || data == null || _contentData.Length != data.Length)
        {
            return false;
        }

        if (ReferenceEquals(_contentData, data))
        {
            return true;
        }

        for (var i = 0; i < _contentData.Length; i++)
        {
            if (data[i] != _contentData[i])
            {
                return false;
            }
        }

        return true;
    }

    internal bool Equals(string path) =>
        _contentPath != null &&
        StringComparer.OrdinalIgnoreCase.Equals(_contentPath, path);

    public bool Equals(BuildFileInfo other) =>
        Equals(other._contentStream) ||
        Equals(other._contentPath) ||
        Equals(other._contentData);

    public override bool Equals(object obj)
        => obj is BuildFileInfo other && Equals(other) && base.Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(base.GetHashCode(), _contentStream, _contentPath, _contentData);
}