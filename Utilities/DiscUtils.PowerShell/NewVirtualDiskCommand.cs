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
using System.Management.Automation;
using DiscUtils.PowerShell.VirtualDiskProvider;

namespace DiscUtils.PowerShell;

[Cmdlet(VerbsCommon.New, "VirtualDisk")]
public class NewVirtualDiskCommand : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0)]
    public string LiteralPath { get; set; }

    [Parameter(Mandatory = true, ParameterSetName="New")]
    [ValidateLength(1,int.MaxValue)]
    public string Type { get; set; }

    [Parameter(Mandatory = true, ParameterSetName="New")]
    public string Size { get; set; }

    [Parameter(Mandatory = true, ParameterSetName = "Diff")]
    public SwitchParameter Differencing { get; set; }

    [Parameter(Mandatory = true, ParameterSetName = "Diff")]
    public string BaseDisk { get; set; }

    protected override void ProcessRecord()
    {
        if (ParameterSetName == "New")
        {
            CreateNewDisk();
        }
        else
        {
            CreateDiffDisk();
        }
    }

    private void CreateNewDisk()
    {
        var typeAndVariant = Type.Split('-');
        if (typeAndVariant.Length is < 1 or > 2)
        {
            WriteError(new ErrorRecord(
                new ArgumentException("Invalid Type of disk"),
                "BadDiskType",
                ErrorCategory.InvalidArgument,
                null));
            return;
        }

        if (!Common.Utilities.TryParseDiskSize(Size, out var size))
        {
            WriteError(new ErrorRecord(
                new ArgumentException("Unable to parse the disk size"),
                "BadDiskSize",
                ErrorCategory.InvalidArgument,
                null));
            return;
        }

        var type = typeAndVariant[0];
        var variant = typeAndVariant.Length > 1 ? typeAndVariant[1] : null;

        var parentObj = ResolveNewDiskPath(out var child);

        VirtualDisk disk;
        if (parentObj.BaseObject is DirectoryInfo dirInfo)
        {
            var path = Path.Combine(dirInfo.FullName, child);
            using (var realDisk = VirtualDisk.CreateDisk(type, variant, path, size, null, null, useAsync: false))
            {
            }

            disk = new OnDemandVirtualDisk(path, FileAccess.ReadWrite);
        }
        else if (parentObj.BaseObject is DiscDirectoryInfo ddi)
        {
            var path = Path.Combine(ddi.FullName, child);
            using (var realDisk = VirtualDisk.CreateDisk(ddi.FileSystem, type, variant, path, size, null, null))
            {
            }

            disk = new OnDemandVirtualDisk(ddi.FileSystem, path, FileAccess.ReadWrite);
        }
        else
        {
            WriteError(new ErrorRecord(
                new DirectoryNotFoundException("Cannot create a virtual disk in that location"),
                "BadDiskLocation",
                ErrorCategory.InvalidArgument,
                null));
            return;
        }

        WriteObject(disk, false);
    }

    private void CreateDiffDisk()
    {
        var parentObj = ResolveNewDiskPath(out var child);

        var baseDiskObj = SessionState.InvokeProvider.Item.Get([BaseDisk], false, true)[0];

        VirtualDisk baseDisk = null;

        try
        {
            if (baseDiskObj.BaseObject is FileInfo fileInfo)
            {
                baseDisk = VirtualDisk.OpenDisk(fileInfo.FullName, FileAccess.Read, useAsync: false);
            }
            else if (baseDiskObj.BaseObject is DiscFileInfo dfi)
            {
                baseDisk = VirtualDisk.OpenDisk(dfi.FileSystem, dfi.FullName, FileAccess.Read);
            }
            else
            {
                WriteError(new ErrorRecord(
                    new FileNotFoundException("The file specified by the BaseDisk parameter doesn't exist"),
                    "BadBaseDiskLocation",
                    ErrorCategory.InvalidArgument,
                    null));
                return;
            }

            VirtualDisk newDisk = null;
            if (parentObj.BaseObject is DirectoryInfo dirInfo)
            {
                var path = Path.Combine(dirInfo.FullName, child);
                using (baseDisk.CreateDifferencingDisk(path, useAsync: false))
                {
                }

                newDisk = new OnDemandVirtualDisk(path, FileAccess.ReadWrite);
            }
            else if (parentObj.BaseObject is DiscDirectoryInfo ddi)
            {
                var path = Path.Combine(ddi.FullName, child);
                using (baseDisk.CreateDifferencingDisk(ddi.FileSystem, path))
                {
                }

                newDisk = new OnDemandVirtualDisk(ddi.FileSystem, path, FileAccess.ReadWrite);
            }
            else
            {
                WriteError(new ErrorRecord(
                    new DirectoryNotFoundException("Cannot create a virtual disk in that location"),
                    "BadDiskLocation",
                    ErrorCategory.InvalidArgument,
                    null));
                return;
            }

            WriteObject(newDisk, false);
        }
        finally
        {
            baseDisk?.Dispose();
        }
    }

    private PSObject ResolveNewDiskPath(out string child)
    {
        PSObject parentObj;

        child = SessionState.Path.ParseChildName(LiteralPath);
        var parent = SessionState.Path.ParseParent(LiteralPath, null);
        var parentPath = SessionState.Path.GetResolvedPSPathFromPSPath(parent)[0];

        parentObj = SessionState.InvokeProvider.Item.Get([parentPath.Path], false, true)[0];

        // If we got a Volume, then we need to send a special marker to the provider indicating that we
        // actually wanted the root directory on the volume, not the volume itself.
        if (parentObj.BaseObject is VolumeInfo)
        {
            parentObj = SessionState.InvokeProvider.Item.Get([Path.Combine(parentPath.Path, @"$Root")], false, true)[0];
        }

        return parentObj;
    }

}
