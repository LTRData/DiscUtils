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
using DiscUtils.Common;
using DiscUtils.Iso9660;
using DiscUtils.Udf;

namespace ISOCreate;

class Program : ProgramBase
{
    private CommandLineParameter _imageFileParam;
    private CommandLineParameter _srcDir;
    private CommandLineParameter _bootImage;
    private CommandLineSwitch _volLabelSwitch;
    private CommandLineSwitch _udfSwitch;

    static void Main(params string[] args)
    {
        var program = new Program();
        program.Run(args);
    }

    protected override StandardSwitches DefineCommandLine(CommandLineParser parser)
    {
        _imageFileParam = new CommandLineParameter("image_file", "The ISO or UDF image file to create.", false);
        _srcDir = new CommandLineParameter("sourcedir", "The directory to be added to the image.", false);
        _bootImage = new CommandLineParameter("bootimage", "The bootable disk image, to create a bootable ISO (unavailable with -udf).", true);
        _volLabelSwitch = new CommandLineSwitch("vl", "vollabel", "label", "Volume label for the image file.");
        _udfSwitch = new CommandLineSwitch("udf", null, "Create a UDF 2.01 image instead of the default ISO 9660 image.");

        parser.AddParameter(_imageFileParam);
        parser.AddParameter(_srcDir);
        parser.AddParameter(_bootImage);
        parser.AddSwitch(_volLabelSwitch);
        parser.AddSwitch(_udfSwitch);

        return StandardSwitches.Default;
    }

    protected override void DoRun()
    {
        var di = new DirectoryInfo(_srcDir.Value);
        if (!di.Exists)
        {
            Console.WriteLine("The source directory doesn't exist!");
            Environment.Exit(1);
        }

        if (_udfSwitch.IsPresent)
        {
            if (_bootImage.IsPresent)
            {
                Console.Error.WriteLine("The bootimage parameter cannot be used with -udf. Boot images are supported only for ISO 9660 images.");
                Environment.ExitCode = 1;
                return;
            }

            var udfBuilder = new UdfBuilder();
            if (_volLabelSwitch.IsPresent)
            {
                udfBuilder.VolumeIdentifier = _volLabelSwitch.Value;
            }

            PopulateFromFolder(di, di.FullName, udfBuilder.AddFile, udfBuilder.AddDirectory);
            udfBuilder.Build(_imageFileParam.Value);
            return;
        }

        var builder = new CDBuilder();

        if (_volLabelSwitch.IsPresent)
        {
            builder.VolumeIdentifier = _volLabelSwitch.Value;
        }

        if (_bootImage.IsPresent)
        {
            builder.SetBootImage(new FileStream(_bootImage.Value, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 2 << 20, useAsync: false), BootDeviceEmulation.NoEmulation, 0);
        }

        PopulateFromFolder(di, di.FullName, (name, sourcePath) => builder.AddFile(name, sourcePath));

        builder.Build(_imageFileParam.Value);
    }

    private static void PopulateFromFolder(DirectoryInfo di, string basePath, Action<string, string> addFile, Action<string> addDirectory = null)
    {
        foreach (var file in di.GetFiles())
        {
            var name = file.FullName[basePath.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            addFile(name, file.FullName);
        }

        foreach (var dir in di.GetDirectories())
        {
            var name = dir.FullName[basePath.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            addDirectory?.Invoke(name);
            PopulateFromFolder(dir, basePath, addFile, addDirectory);
        }
    }
}
