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

using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DiscUtils.Common;

public class CommandLineMultiParameter
{
    private string _name;
    private string _description;
    private bool _isOptional;

    private bool _isPresent;
    private List<string> _values;

    public CommandLineMultiParameter(string name, string description, bool isOptional)
    {
        _name = name;
        _description = description;
        _isOptional = isOptional;
        _values = new List<string>();
    }

    public bool IsPresent => _isPresent;

    public string[] Values => _values.ToArray();

    public virtual bool IsValid => _isOptional || _isPresent;

    internal bool IsOptional => _isOptional;

    internal string CommandLineText => _isOptional ? $"[{_name}0 {_name}1 ...]" : $"{_name}0 {_name}1 ...";

    internal int NameDisplayLength => _name.Length + 4;

    internal void WriteDescription(TextWriter writer, string lineTemplate, int perLineDescWidth)
    {
        var text = Utilities.WordWrap((_isOptional ? "Optional. " : "") + _description, perLineDescWidth).ToArray();

        writer.WriteLine(lineTemplate, $"{_name}0..n", text[0]);
        for (var i = 1; i < text.Length; ++i)
        {
            writer.WriteLine(lineTemplate, "", text[i]);
        }
    }

    protected internal virtual bool Matches(string arg)
    {
        return true;
    }

    protected internal virtual int Process(string[] args, int pos)
    {
        _isPresent = true;
        _values.Add(args[pos]);
        return pos + 1;
    }
}
