//
// Copyright (c) 2024, Olof Lagerkvist and contributors
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
namespace DiscUtils.SquashFs;

/// <summary>
/// The compression algorithm used in the SquashFs file system.
/// </summary>
public enum SquashFileSystemCompression : ushort
{
    /// <summary>
    /// The compression algorithm is unknown.
    /// </summary>
    Unknown,

    /// <summary>
    /// The compression algorithm is ZLib.
    /// </summary>
    ZLib,

    /// <summary>
    /// The compression algorithm is Lzma.
    /// </summary>
    Lzma,

    /// <summary>
    /// The compression algorithm is Lzo.
    /// </summary>
    Lzo,

    /// <summary>
    /// The compression algorithm is Xz.
    /// </summary>
    Xz,

    /// <summary>
    /// The compression algorithm is Lz4.
    /// </summary>
    Lz4,

    /// <summary>
    /// The compression algorithm is ZStd.
    /// </summary>
    ZStd
}