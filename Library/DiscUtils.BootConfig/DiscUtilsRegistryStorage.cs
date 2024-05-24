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
using System.Collections.Generic;
using System.Globalization;
using DiscUtils.Registry;

namespace DiscUtils.BootConfig;

internal class DiscUtilsRegistryStorage : BaseStorage
{
    private readonly RegistryKey _rootKey;

    public DiscUtilsRegistryStorage(RegistryKey key)
    {
        _rootKey = key;
    }

    public override string GetString(Guid obj, int element)
    {
        return GetValue(obj, element) as string;
    }

    public override void SetString(Guid obj, int element, string value)
    {
        SetValue(obj, element, value);
    }

    public override byte[] GetBinary(Guid obj, int element)
    {
        return GetValue(obj, element) as byte[];
    }

    public override void SetBinary(Guid obj, int element, byte[] value)
    {
        SetValue(obj, element, value);
    }

    public override string[] GetMultiString(Guid obj, int element)
    {
        return GetValue(obj, element) as string[];
    }

    public override void SetMultiString(Guid obj, int element, string[] values)
    {
        SetValue(obj, element, values);
    }

    public override IEnumerable<Guid> EnumerateObjects()
    {
        var parentKey = _rootKey.OpenSubKey("Objects");
        foreach (var key in parentKey.GetSubKeyNames())
        {
            yield return new Guid(key);
        }
    }

    public override IEnumerable<int> EnumerateElements(Guid obj)
    {
        var path = $@"Objects\{obj:B}\Elements";
        var parentKey = _rootKey.OpenSubKey(path);
        foreach (var key in parentKey.GetSubKeyNames())
        {
            yield return int.Parse(key, NumberStyles.HexNumber);
        }
    }

    public override int GetObjectType(Guid obj)
    {
        var path = $@"Objects\{obj:B}\Description";

        var descKey = _rootKey.OpenSubKey(path);

        var val = descKey.GetValue("Type");
        return (int)val;
    }

    public override bool HasValue(Guid obj, int element)
    {
        var path = $@"Objects\{obj:B}\Elements\{element:X8}";
        return _rootKey.OpenSubKey(path) != null;
    }

    public override bool ObjectExists(Guid obj)
    {
        var path = $@"Objects\{obj:B}\Description";

        return _rootKey.OpenSubKey(path) != null;
    }

    public override Guid CreateObject(Guid obj, int type)
    {
        var realObj = obj == Guid.Empty ? Guid.NewGuid() : obj;
        var path = $@"Objects\{realObj:B}\Description";

        var key = _rootKey.CreateSubKey(path);
        key.SetValue("Type", type, RegistryValueType.Dword);

        return realObj;
    }

    public override void CreateElement(Guid obj, int element)
    {
        var path = $@"Objects\{obj:B}\Elements\{element:X8}";

        _rootKey.CreateSubKey(path);
    }

    public override void DeleteObject(Guid obj)
    {
        var path = $@"Objects\{obj:B}\Description";

        _rootKey.DeleteSubKeyTree(path);
    }

    public override void DeleteElement(Guid obj, int element)
    {
        var path = $@"Objects\{obj:B}\Elements\{element:X8}";

        _rootKey.DeleteSubKeyTree(path);
    }

    private object GetValue(Guid obj, int element)
    {
        var path = $@"Objects\{obj:B}\Elements\{element:X8}";
        var key = _rootKey.OpenSubKey(path);
        return key.GetValue("Element");
    }

    private void SetValue(Guid obj, int element, object value)
    {
        var path = $@"Objects\{obj:B}\Elements\{element:X8}";
        var key = _rootKey.OpenSubKey(path);
        key.SetValue("Element", value);
    }
}