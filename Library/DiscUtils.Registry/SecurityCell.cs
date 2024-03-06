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
using DiscUtils.Core.WindowsSecurity.AccessControl;
using DiscUtils.Streams;

namespace DiscUtils.Registry;

internal sealed class SecurityCell : Cell
{
    public SecurityCell(RegistrySecurity secDesc)
        : this(-1)
    {
        SecurityDescriptor = secDesc;
    }

    public SecurityCell(int index)
        : base(index)
    {
        PreviousIndex = -1;
        NextIndex = -1;
    }

    public int NextIndex { get; set; }

    public int PreviousIndex { get; set; }

    public RegistrySecurity SecurityDescriptor { get; private set; }

    public override int Size
    {
        get
        {
            var sdLen = SecurityDescriptor.BinaryLength;
            return 0x14 + sdLen;
        }
    }

    public int UsageCount { get; set; }

    public override int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        PreviousIndex = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x04));
        NextIndex = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x08));
        UsageCount = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x0C));
        var secDescSize = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x10));

        var secDesc = buffer.Slice(0x14, secDescSize);
        SecurityDescriptor = new RegistrySecurity(secDesc);

        return 0x14 + secDescSize;
    }

    public override void WriteTo(Span<byte> buffer)
    {
        var latin1Encoding = EncodingUtilities.GetLatin1Encoding();
        var sd = SecurityDescriptor.GetSecurityDescriptorBinaryForm();

        "sk"u8.CopyTo(buffer.Slice(0, 2));
        EndianUtilities.WriteBytesLittleEndian(PreviousIndex, buffer.Slice(0x04));
        EndianUtilities.WriteBytesLittleEndian(NextIndex, buffer.Slice(0x08));
        EndianUtilities.WriteBytesLittleEndian(UsageCount, buffer.Slice(0x0C));
        EndianUtilities.WriteBytesLittleEndian(sd.Length, buffer.Slice(0x10));
        sd.CopyTo(buffer.Slice(0x14));
    }

    public override string ToString()
    {
        return $"SecDesc:{SecurityDescriptor.GetSddlForm(AccessControlSections.All)} (refCount:{UsageCount})";
    }
}