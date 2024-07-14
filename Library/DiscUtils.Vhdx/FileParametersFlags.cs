//
// Copyright (c) 2008-2012, Kenneth Bell
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

namespace DiscUtils.Vhdx;

[Flags]
internal enum FileParametersFlags : uint
{
    /// <summary>
    /// If no bits are set, the disk will be considered Dynamic.
    /// </summary>
    None = 0,
    /// <summary>
    /// If set tells system to leave blocks allocated(fixed disk), otherwise it is a dynamic/differencing disk.
    /// </summary>
    LeaveBlocksAllocated = 1, //if bit 0 is set, the file is a fixed VHD, otherwise it is a dynamic/differencing VHDX.
    /// <summary>
    /// If this bit is set the file has a parent file, so it's most likely a differencing disk.
    /// </summary>
    HasParent = 2,
}