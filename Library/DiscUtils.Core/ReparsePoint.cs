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
using System.IO;
using System.Text;

namespace DiscUtils;

/// <summary>
/// Represents a Reparse Point, which can be associated with a file or directory.
/// </summary>
public sealed class ReparsePoint
{
    /// <summary>
    /// Initializes a new instance of the ReparsePoint class.
    /// </summary>
    /// <param name="tag">The defined reparse point tag.</param>
    /// <param name="content">The reparse point's content.</param>
    public ReparsePoint(int tag, byte[] content)
    {
        Tag = tag;
        Content = content;
    }

    /// <summary>
    /// Gets or sets the reparse point's content.
    /// </summary>
    public byte[] Content { get; set; }

    /// <summary>
    /// Gets or sets the defined reparse point tag.
    /// </summary>
    public int Tag { get; set; }
	// https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-fscc/c8e77b37-3909-4fe6-a4ea-2b9d423b1ee4
	private const int IO_REPARSE_TAG_MOUNT_POINT = unchecked((int)0xA0000003);
	private const int IO_REPARSE_TAG_SYMLINK = unchecked((int)0xA000000C);
	// https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-fscc/b41f1cbf-10df-4a47-98d4-1c52a833d913
	private enum SymlinkFlags : int {
		 FullpathName = 0,
		 SYMLINK_FLAG_RELATIVE = 1
	}
	internal string ParseSymlink(String originalPath) {
		
		var reparsePoint = this;

		using var stream = new MemoryStream(reparsePoint.Content);
		using var reader = new BinaryReader(stream);
		if (reparsePoint.Tag != IO_REPARSE_TAG_SYMLINK && reparsePoint.Tag != IO_REPARSE_TAG_MOUNT_POINT)
			throw new IOException($"Reparse point on {originalPath} is not a symlink or mount point (tag: 0x{reparsePoint.Tag:X8})");

		var substNameOffset = reader.ReadUInt16();
		var substNameLength = reader.ReadUInt16();
		var printNameOffset = reader.ReadUInt16();
		var printNameLength = reader.ReadUInt16();
		SymlinkFlags? flags = null;
		if (reparsePoint.Tag == IO_REPARSE_TAG_SYMLINK)
			flags = (SymlinkFlags)reader.ReadUInt32();


		string target;
		// Prefer PrintName if available
		if (printNameLength > 0) {
			stream.Seek(printNameOffset, SeekOrigin.Current);
			var pathBytes = reader.ReadBytes(printNameLength);
			target = Encoding.Unicode.GetString(pathBytes);
		} else {
			stream.Seek(substNameOffset, SeekOrigin.Current);
			var pathBytes = reader.ReadBytes(substNameLength);
			target = Encoding.Unicode.GetString(pathBytes);
			// alternatives I have done additional cleaning but for here we may want raw values: https://github.com/mitchcapper/gnulib/blob/b5c3b1b1f1fe6225363cddd72310e1fe95312466/lib/readlink.c#L115-#L182 but the use case here may be a bit different
		}
		return target;
	}
}
