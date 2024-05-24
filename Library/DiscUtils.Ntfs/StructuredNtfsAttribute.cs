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
using System.Buffers;
using System.IO;
using DiscUtils.Streams;
using LTRData.Extensions.Formatting;

namespace DiscUtils.Ntfs;

internal class StructuredNtfsAttribute<T> : NtfsAttribute
    where T : class, IByteArraySerializable, IDiagnosticTraceable, new()
{
    private bool _hasContent;
    private bool _initialized;
    private T _structure;

    public StructuredNtfsAttribute(File file, FileRecordReference containingFile, AttributeRecord record)
        : base(file, containingFile, record)
    {
        _structure = new T();
    }

    public T Content
    {
        get
        {
            Initialize();
            return _structure;
        }

        set
        {
            _structure = value;
            _hasContent = true;
        }
    }

    public bool HasContent
    {
        get
        {
            Initialize();
            return _hasContent;
        }
    }

    public void Save()
    {
        byte[] allocated = null;

        var buffer = _structure.Size <= 1024
            ? stackalloc byte[_structure.Size]
            : (allocated = ArrayPool<byte>.Shared.Rent(_structure.Size)).AsSpan(0, _structure.Size);

        try
        {
            _structure.WriteTo(buffer);
            using var s = Open(FileAccess.Write);
            s.Write(buffer);
            s.SetLength(buffer.Length);
        }
        finally
        {
            if (allocated is not null)
            {
                ArrayPool<byte>.Shared.Return(allocated);
            }
        }
    }

    public override string ToString()
    {
        Initialize();
        return _structure.ToString();
    }

    public override void Dump(TextWriter writer, string indent)
    {
        try
        {
            Initialize();
            writer.WriteLine($"{indent}{AttributeTypeName} ATTRIBUTE ({Name ?? "No Name"})");
            _structure.Dump(writer, $"{indent}  ");

            _primaryRecord.Dump(writer, $"{indent}  ");
        }
        catch (Exception ex)
        {
            writer.WriteLine($"{indent}{AttributeTypeName} ATTRIBUTE: {ex.JoinMessages()}");
        }
    }

    private void Initialize()
    {
        if (!_initialized)
        {
            using var s = Open(FileAccess.Read);
            _structure.ReadFrom(s, (int)Length);
            _hasContent = s.Length != 0;
            _initialized = true;
        }
    }
}
