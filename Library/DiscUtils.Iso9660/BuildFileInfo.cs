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

    internal BuildFileInfo(string name, BuildDirectoryInfo parent, byte[] content)
        : base(IsoUtilities.NormalizeFileName(name), MakeShortFileName(name))
    {
        if (content.LongLength >= (4L << 30))
        {
            throw new InvalidOperationException("ISO 9660 file system does not support files larger than 4 GB");
        }

        Parent = parent;
        _contentData = content;
    }

    internal BuildFileInfo(string name, BuildDirectoryInfo parent, string content)
        : base(IsoUtilities.NormalizeFileName(name), MakeShortFileName(name))
    {
        if (new FileInfo(content).Length >= (4L << 30))
        {
            throw new InvalidOperationException("ISO 9660 file system does not support files larger than 4 GB");
        }

        Parent = parent;
        _contentPath = content;

        CreationTime = new FileInfo(_contentPath).LastWriteTimeUtc;
    }

    internal BuildFileInfo(string name, BuildDirectoryInfo parent, Stream source)
        : base(IsoUtilities.NormalizeFileName(name), MakeShortFileName(name))
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

    private static string MakeShortFileName(string longName)
    {
        if (IsoUtilities.IsValidFileName(longName))
        {
            return longName;
        }

        var shortNameChars = longName.ToUpperInvariant().ToCharArray();
        for (var i = 0; i < shortNameChars.Length; ++i)
        {
            if (!IsoUtilities.IsValidDChar(shortNameChars[i]) && shortNameChars[i] != '.' && shortNameChars[i] != ';')
            {
                shortNameChars[i] = '_';
            }
        }

        var parts = IsoUtilities.SplitFileName(new string(shortNameChars));

        if (parts[0].Length + parts[1].Length > 30)
        {
            parts[1] = parts[1].Substring(0, Math.Min(parts[1].Length, 3));
        }

        if (parts[0].Length + parts[1].Length > 30)
        {
            parts[0] = parts[0].Substring(0, 30 - parts[1].Length);
        }

        var candidate = $"{parts[0]}.{parts[1]};{parts[2]}";

        // TODO: Make unique
        return candidate;
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