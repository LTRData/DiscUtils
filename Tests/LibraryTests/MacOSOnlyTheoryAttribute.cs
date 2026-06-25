//
// Copyright (c) 2020, Quamotion bv
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
using System.Linq;
using System.Runtime.InteropServices;

namespace LibraryTests;

public class MacOSOnlyTheoryAttribute : TheoryAttribute
{
    public override string? Skip
    {
        get
        {
#if NETCOREAPP
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "This test runs on macOS only";
            }

            return null;
#else
            return "This test runs on macOS only";
#endif
        }
        set => throw new NotSupportedException();
    }
}

public class MacOSOnlyFactAttribute : FactAttribute
{
    public static string? DeveloperImage => field ??= (Environment.GetEnvironmentVariable("DEVELOPER_DISK_IMAGE")
         ?? FindDeveloperDiskImage());

    private static string? FindDeveloperDiskImage()
    {
        const string xcode = "/Applications/Xcode.app";

        if (!Directory.Exists(xcode))
            return null;

        return Directory.EnumerateFiles(
            xcode,
            "DeveloperDiskImage.dmg",
            SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    public override string? Skip
    {
        get
        {
#if NETCOREAPP
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "This test runs on macOS only";
            }

            if (DeveloperImage is null)
            {
                return "DeveloperDiskImage.dmg not available";
            }

            return null;
#else
            return "This test runs on macOS only";
#endif
        }
        set => throw new NotSupportedException();
    }
}
