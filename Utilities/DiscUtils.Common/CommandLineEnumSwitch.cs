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

namespace DiscUtils.Common;

public class CommandLineEnumSwitch<T> : CommandLineSwitch
    where T : struct, IConvertible
{
    private T _defaultValue;
    private T _enumValue;

    public CommandLineEnumSwitch(string fullSwitch, string paramName, T defaultValue, string description)
        : base(fullSwitch, paramName, description)
    {
        _defaultValue = defaultValue;
        _enumValue = defaultValue;
    }

    public CommandLineEnumSwitch(string shortSwitch, string fullSwitch, string paramName, T defaultValue, string description)
        : base(shortSwitch, fullSwitch, paramName, description)
    {
        _defaultValue = defaultValue;
        _enumValue = defaultValue;
    }

    public CommandLineEnumSwitch(string[] shortSwitches, string fullSwitch, string paramName, T defaultValue, string description)
        : base(shortSwitches, fullSwitch, paramName, description)
    {
        _defaultValue = defaultValue;
        _enumValue = defaultValue;
    }

    public override string FullDescription
    {
        get {
            var vals = Enum.GetNames(typeof(T));

            if (vals.Length < 3)
            {
                return $"Either '{vals[0]}' or '{vals[1]}' (default is {_defaultValue}).  {base.FullDescription}";
            }
            else
            {
                return $"One of '{string.Join(", ", vals)}' (default is {_defaultValue}).  {base.FullDescription}";
            }
        }
    }

    public T EnumValue => _enumValue;

    internal override int Process(string[] args, int pos)
    {
        var retVal = base.Process(args, pos);

        _enumValue = _defaultValue;
        try
        {
            _enumValue = (T)Enum.Parse(typeof(T), Value, true);
        }
        catch
        {
            throw new Exception($"Incorrect value for parameter '{FullSwitchName}', must be one of '{string.Join(", ", Enum.GetNames(typeof(T)))}'");
        }

        return retVal;
    }
}
