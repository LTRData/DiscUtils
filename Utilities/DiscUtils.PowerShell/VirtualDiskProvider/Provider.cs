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

using DiscUtils.Streams.Compatibility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Provider;
using System.Text;
using DiscUtils.Complete;
using DiscUtils.Ntfs;
using LTRData.Extensions.Buffers;

namespace DiscUtils.PowerShell.VirtualDiskProvider;

[CmdletProvider("VirtualDisk", ProviderCapabilities.Credentials)]
public sealed class Provider : NavigationCmdletProvider, IContentCmdletProvider
{
    #region Drive manipulation
    protected override PSDriveInfo NewDrive(PSDriveInfo drive)
    {
        SetupHelper.SetupComplete();

        var dynParams = DynamicParameters as NewDriveParameters;

        if (drive == null)
        {
            WriteError(new ErrorRecord(
                new ArgumentNullException(nameof(drive)),
                "NullDrive",
                ErrorCategory.InvalidArgument,
                null));
            return null;
        }

        if (string.IsNullOrEmpty(drive.Root))
        {
            WriteError(new ErrorRecord(
                new ArgumentException("drive"),
                "NoRoot",
                ErrorCategory.InvalidArgument,
                drive));
            return null;
        }

        var mountPaths = Utilities.NormalizePath(drive.Root).Split('!');
        if (mountPaths.Length is < 1 or > 2)
        {
            WriteError(new ErrorRecord(
                new ArgumentException("drive"),
                "InvalidRoot",
                ErrorCategory.InvalidArgument,
                drive));
            //return null;
        }

        var diskPath = mountPaths[0];
        var relPath = mountPaths.Length > 1 ? mountPaths[1] : "";

        string user = null;
        string password = null;
        if (drive.Credential != null && drive.Credential.UserName != null)
        {
            var netCred = drive.Credential.GetNetworkCredential();
            user = netCred.UserName;
            password = netCred.Password;
        }

        try
        {
            var fullPath = Utilities.DenormalizePath(diskPath);
            var resolvedPath = SessionState.Path.GetResolvedPSPathFromPSPath(fullPath)[0];
            if (resolvedPath.Provider.Name == "FileSystem")
            {
                fullPath = resolvedPath.ProviderPath;
            }

            var access = dynParams.ReadWrite.IsPresent ? FileAccess.ReadWrite : FileAccess.Read;
            var disk = VirtualDisk.OpenDisk(fullPath, dynParams.DiskType, access, user, password, useAsync: false);
            return new VirtualDiskPSDriveInfo(drive, MakePath($"{Utilities.NormalizePath(fullPath)}!", relPath), disk);
        }
        catch (IOException ioe)
        {
            WriteError(new ErrorRecord(
                ioe,
                "DiskAccess",
                ErrorCategory.ResourceUnavailable,
                drive.Root));
            return null;
        }
    }

    protected override object NewDriveDynamicParameters()
    {
        return new NewDriveParameters();
    }

    protected override PSDriveInfo RemoveDrive(PSDriveInfo drive)
    {
        if (drive == null)
        {
            WriteError(new ErrorRecord(
                new ArgumentNullException(nameof(drive)),
                "NullDrive",
                ErrorCategory.InvalidArgument,
                null));
            return null;
        }

        var vdDrive = drive as VirtualDiskPSDriveInfo;
        if (vdDrive == null)
        {
            WriteError(new ErrorRecord(
                new ArgumentException("invalid type of drive"),
                "BadDrive",
                ErrorCategory.InvalidArgument,
                null));
            return null;
        }

        vdDrive.Disk.Dispose();

        return vdDrive;
    }
    #endregion

    #region Item methods
    protected override void GetItem(string path)
    {
        var readOnly = !(DynamicParameters is GetItemParameters dynParams && dynParams.ReadWrite.IsPresent);

        var obj = FindItemByPath(Utilities.NormalizePath(path), false, readOnly);
        if (obj != null)
        {
            WriteItemObject(obj, path.Trim(Path.DirectorySeparatorChar), true);
        }
    }

    protected override object GetItemDynamicParameters(string path)
    {
        return new GetItemParameters();
    }

    protected override void SetItem(string path, object value)
    {
        throw new NotImplementedException();
    }

    protected override bool ItemExists(string path)
    {
        var result = FindItemByPath(Utilities.NormalizePath(path), false, true) != null;
        return result;
    }

    protected override bool IsValidPath(string path)
    {
        return !string.IsNullOrEmpty(path);
    }
    #endregion

    #region Container methods
    protected override void GetChildItems(string path, bool recurse)
    {
        GetChildren(Utilities.NormalizePath(path), recurse, false);
    }

    protected override void GetChildNames(string path, ReturnContainers returnContainers)
    {
        // TODO: returnContainers
        GetChildren(Utilities.NormalizePath(path), false, true);
    }

    protected override bool HasChildItems(string path)
    {
        var obj = FindItemByPath(Utilities.NormalizePath(path), true, true);

        if (obj is DiscFileInfo)
        {
            return false;
        }
        else if (obj is DiscDirectoryInfo info)
        {
            return info.GetFileSystemInfos().Any();
        }
        else
        {
            return true;
        }
    }

    protected override void RemoveItem(string path, bool recurse)
    {
        var obj = FindItemByPath(Utilities.NormalizePath(path), false, false);

        if (obj is DiscDirectoryInfo discDirInfo)
        {
            discDirInfo.Delete(true);
        }
        else if (obj is DiscFileInfo discFileInfo)
        {
            discFileInfo.Delete();
        }
        else
        {
            WriteError(new ErrorRecord(
                new InvalidOperationException($"Cannot delete items of this type: {obj?.GetType()}"),
                "UnknownObjectTypeToRemove",
                ErrorCategory.InvalidOperation,
                obj));
        }
    }

    protected override void NewItem(string path, string itemTypeName, object newItemValue)
    {
        var parentPath = GetParentPath(path, null);

        if (string.IsNullOrEmpty(itemTypeName))
        {
            WriteError(new ErrorRecord(
                new InvalidOperationException("No type specified.  Specify \"file\" or \"directory\" as the type."),
                "NoTypeForNewItem",
                ErrorCategory.InvalidArgument,
                itemTypeName));
            return;
        }

        var itemTypeUpper = itemTypeName.ToUpperInvariant();

        var obj = FindItemByPath(Utilities.NormalizePath(parentPath), true, false);

        if (obj is DiscDirectoryInfo dirInfo)
        {
            if (itemTypeUpper == "FILE")
            {
                using (dirInfo.FileSystem.OpenFile(Path.Combine(dirInfo.FullName, GetChildName(path)), FileMode.Create))
                {
                }
            }
            else if (itemTypeUpper == "DIRECTORY")
            {
                dirInfo.FileSystem.CreateDirectory(Path.Combine(dirInfo.FullName, GetChildName(path)));
            }
            else if (itemTypeUpper == "HARDLINK")
            {
                if (dirInfo.FileSystem is NtfsFileSystem ntfs)
                {
                    var hlParams = (NewHardLinkDynamicParameters)DynamicParameters;

                    var srcItems = SessionState.InvokeProvider.Item.Get(hlParams.SourcePath);
                    if (srcItems.Count != 1)
                    {
                        WriteError(new ErrorRecord(
                            new InvalidOperationException("The type is unknown for this provider.  Only \"file\" and \"directory\" can be specified."),
                            "UnknownTypeForNewItem",
                            ErrorCategory.InvalidArgument,
                            itemTypeName));
                        return;
                    }

                    var srcFsi = srcItems[0].BaseObject as DiscFileSystemInfo;

                    ntfs.CreateHardLink(srcFsi.FullName, Path.Combine(dirInfo.FullName, GetChildName(path)));
                }
            }
            else
            {
                WriteError(new ErrorRecord(
                    new InvalidOperationException("The type is unknown for this provider.  Only \"file\" and \"directory\" can be specified."),
                    "UnknownTypeForNewItem",
                    ErrorCategory.InvalidArgument,
                    itemTypeName));
                return;
            }
        }
        else
        {
            WriteError(new ErrorRecord(
                new InvalidOperationException($"Cannot create items in an object of this type: {obj?.GetType()}"),
                "UnknownObjectTypeForNewItemParent",
                ErrorCategory.InvalidOperation,
                obj));
            return;
        }
    }

    protected override object NewItemDynamicParameters(string path, string itemTypeName, object newItemValue)
    {
        if (string.IsNullOrEmpty(itemTypeName))
        {
            return null;
        }

        var itemTypeUpper = itemTypeName.ToUpperInvariant();

        if (itemTypeUpper == "HARDLINK")
        {
            return new NewHardLinkDynamicParameters();
        }

        return null;
    }

    protected override void RenameItem(string path, string newName)
    {
        var obj = FindItemByPath(Utilities.NormalizePath(path), true, false);

        if (obj is not DiscFileSystemInfo fsiObj)
        {
            WriteError(new ErrorRecord(
                new InvalidOperationException("Cannot move items to this location"),
                "BadParentForNewItem",
                ErrorCategory.InvalidArgument,
                newName));
            return;
        }

        var newFullName = Path.Combine(Path.GetDirectoryName(fsiObj.FullName.TrimEnd(Internal.Utilities.PathSeparators)), newName);

        if (obj is DiscDirectoryInfo dirObj)
        {
            dirObj.MoveTo(newFullName);
        }
        else
        {
            var fileObj = obj as DiscFileInfo;
            fileObj.MoveTo(newFullName);
        }
    }

    protected override void CopyItem(string path, string copyPath, bool recurse)
    {
        string destFileName = null;

        var destObj = FindItemByPath(Utilities.NormalizePath(copyPath), true, false);
        var destDir = destObj as DiscDirectoryInfo;
        if (destDir != null)
        {
            destFileName = GetChildName(path);
        }
        else if (destObj is null or DiscFileInfo)
        {
            destObj = FindItemByPath(Utilities.NormalizePath(GetParentPath(copyPath, null)), true, false);
            destDir = destObj as DiscDirectoryInfo;
            destFileName = GetChildName(copyPath);
        }

        if (destDir == null)
        {
            WriteError(new ErrorRecord(
                new InvalidOperationException("Cannot copy items to this location"),
                "BadParentForNewItem",
                ErrorCategory.InvalidArgument,
                copyPath));
            return;
        }

        var srcDirObj = FindItemByPath(Utilities.NormalizePath(GetParentPath(path, null)), true, true);
        var srcFileName = GetChildName(path);
        if (srcDirObj is not DiscDirectoryInfo srcDir)
        {
            WriteError(new ErrorRecord(
                new InvalidOperationException("Cannot copy items from this location"),
                "BadParentForNewItem",
                ErrorCategory.InvalidArgument,
                copyPath));
            return;
        }

        DoCopy(srcDir, srcFileName, destDir, destFileName, recurse);
    }
    #endregion

    #region Navigation methods
    protected override bool IsItemContainer(string path)
    {
        var obj = FindItemByPath(Utilities.NormalizePath(path), false, true);

        var result = false;
        if (obj is VirtualDisk)
        {
            result = true;
        }
        else if (obj is LogicalVolumeInfo)
        {
            result = true;
        }
        else if (obj is DiscDirectoryInfo)
        {
            result = true;
        }

        return result;
    }

    protected override string MakePath(string parent, string child)
    {
        return Utilities.NormalizePath(base.MakePath(Utilities.DenormalizePath(parent), Utilities.DenormalizePath(child)));
    }

    #endregion

    #region IContentCmdletProvider Members

    public void ClearContent(string path)
    {
        var destObj = FindItemByPath(Utilities.NormalizePath(path), true, false);
        if (destObj is DiscFileInfo discFileInfo)
        {
            using var s = discFileInfo.Open(FileMode.Open, FileAccess.ReadWrite);
            s.SetLength(0);
        }
        else
        {
            WriteError(new ErrorRecord(
                new IOException("Cannot write to this item"),
                "BadContentDestination",
                ErrorCategory.InvalidOperation,
                destObj));
        }
    }

    public object ClearContentDynamicParameters(string path)
    {
        return null;
    }

    public IContentReader GetContentReader(string path)
    {
        var destObj = FindItemByPath(Utilities.NormalizePath(path), true, false);
        if (destObj is DiscFileInfo discFileInfo)
        {
            return new FileContentReaderWriter(
                this,
                discFileInfo.Open(FileMode.Open, FileAccess.Read),
                DynamicParameters as ContentParameters);
        }
        else
        {
            WriteError(new ErrorRecord(
                new IOException("Cannot read from this item"),
                "BadContentSource",
                ErrorCategory.InvalidOperation,
                destObj));
            return null;
        }
    }

    public object GetContentReaderDynamicParameters(string path)
    {
        return new ContentParameters();
    }

    public IContentWriter GetContentWriter(string path)
    {
        var destObj = FindItemByPath(Utilities.NormalizePath(path), true, false);
        if (destObj is DiscFileInfo discFileInfo)
        {
            return new FileContentReaderWriter(
                this,
                discFileInfo.Open(FileMode.Open, FileAccess.ReadWrite),
                DynamicParameters as ContentParameters);
        }
        else
        {
            WriteError(new ErrorRecord(
                new IOException("Cannot write to this item"),
                "BadContentDestination",
                ErrorCategory.InvalidOperation,
                destObj));
            return null;
        }
    }

    public object GetContentWriterDynamicParameters(string path)
    {
        return new ContentParameters();
    }

    #endregion

    #region Type Extensions
    public static string Mode(PSObject instance)
    {
        if (instance == null)
        {
            return "";
        }

        if (instance.BaseObject is not DiscFileSystemInfo fsi)
        {
            return "";
        }

        var result = new StringBuilder(5);
        result.Append(((fsi.Attributes & FileAttributes.Directory) != 0) ? "d" : "-");
        result.Append(((fsi.Attributes & FileAttributes.Archive) != 0) ? "a" : "-");
        result.Append(((fsi.Attributes & FileAttributes.ReadOnly) != 0) ? "r" : "-");
        result.Append(((fsi.Attributes & FileAttributes.Hidden) != 0) ? "h" : "-");
        result.Append(((fsi.Attributes & FileAttributes.System) != 0) ? "s" : "-");
        return result.ToString();
    }
    #endregion

    private VirtualDiskPSDriveInfo DriveInfo => PSDriveInfo as VirtualDiskPSDriveInfo;

    private VirtualDisk Disk
    {
        get
        {
            var driveInfo = DriveInfo;
            return driveInfo?.Disk;
        }
    }

    private object FindItemByPath(string path, bool preferFs, bool readOnly)
    {
        var fileAccess = readOnly ? FileAccess.Read : FileAccess.ReadWrite;
        string diskPath;
        string relPath;

        var mountSepIdx = path.IndexOf('!');
        if (mountSepIdx < 0)
        {
            diskPath = path;
            relPath = "";
        }
        else
        {
            diskPath = path.Substring(0, mountSepIdx);
            relPath = path.Substring(mountSepIdx + 1);
        }

        var disk = Disk;
        if( disk == null )
        {
            var odvd = new OnDemandVirtualDisk(Utilities.DenormalizePath(diskPath), fileAccess);
            if (odvd.IsValid)
            {
                disk = odvd;
                ShowSlowDiskWarning();
            }
            else
            {
                return null;
            }
        }

        var pathElems = new List<string>(relPath.Split(Internal.Utilities.PathSeparators, StringSplitOptions.RemoveEmptyEntries));

        if (pathElems.Count == 0)
        {
            return disk;
        }

        var volMgr = DriveInfo != null ? DriveInfo.VolumeManager : new VolumeManager(disk);
        var volumes = volMgr.GetLogicalVolumes();
        var volNumStr = pathElems[0].StartsWith("Volume", StringComparison.OrdinalIgnoreCase) ? pathElems[0].Substring(6) : null;

        VolumeInfo volInfo;
        if (int.TryParse(volNumStr, out var volNum) || volNum < 0 || volNum >= volumes.Length)
        {
            volInfo = volumes[volNum];
        }
        else
        {
            volInfo = volMgr.GetVolume(Utilities.DenormalizePath(pathElems[0]));
        }

        pathElems.RemoveAt(0);
        if (volInfo == null || (pathElems.Count == 0 && !preferFs))
        {
            return volInfo;
        }

        var fs = GetFileSystem(volInfo, out var disposeFs);
        try
        {
            if (fs == null)
            {
                return null;
            }

            // Special marker in the path - disambiguates the root folder from the volume
            // containing it.  By this point it's done it's job (we didn't return volInfo),
            // so we just remove it.
            if (pathElems.Count > 0 && pathElems[0] == "$Root")
            {
                pathElems.RemoveAt(0);
            }

            var fsPath = string.Join(Internal.Utilities.DirectorySeparatorString, pathElems);
            if (fs.DirectoryExists(fsPath))
            {
                return fs.GetDirectoryInfo(fsPath);
            }
            else if (fs.FileExists(fsPath))
            {
                return fs.GetFileInfo(fsPath);
            }
        }
        finally
        {
            if (disposeFs && fs != null)
            {
                fs.Dispose();
            }
        }

        return null;
    }

    private void ShowSlowDiskWarning()
    {
        const string varName = "DiscUtils_HideSlowDiskWarning";
        var psVar = SessionState.PSVariable.Get(varName);
        if (psVar != null && psVar.Value != null)
        {

            var valStr = psVar.Value.ToString();
            if (bool.TryParse(valStr, out var warningHidden) && warningHidden)
            {
                return;
            }
        }

        WriteWarning("Slow disk access.  Mount the disk using New-PSDrive to improve performance.  This message will not show again.");
        SessionState.PSVariable.Set(varName, true.ToString());
    }

    private DiscFileSystem GetFileSystem(VolumeInfo volInfo, out bool dispose)
    {
        if (DriveInfo != null)
        {
            dispose = false;
            return DriveInfo.GetFileSystem(volInfo);
        }
        else
        {
            // TODO: proper file system detection
            if (volInfo.BiosType == 7)
            {
                dispose = true;
                return new NtfsFileSystem(volInfo.Open());
            }
        }

        dispose = false;
        return null;
    }

    private void GetChildren(string path, bool recurse, bool namesOnly)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var obj = FindItemByPath(path, false, true);

        if (obj is VirtualDisk vd)
        {
            EnumerateDisk(vd, path, recurse, namesOnly);
        }
        else if (obj is LogicalVolumeInfo lvi)
        {
            var fs = GetFileSystem(lvi, out var dispose);
            try
            {
                if (fs != null)
                {
                    EnumerateDirectory(fs.Root, path, recurse, namesOnly);
                }
            }
            finally
            {
                if (dispose && fs != null)
                {
                    fs.Dispose();
                }
            }
        }
        else if (obj is DiscDirectoryInfo ddi)
        {
            EnumerateDirectory(ddi, path, recurse, namesOnly);
        }
        else
        {
            WriteError(new ErrorRecord(
                new InvalidOperationException($"Unrecognized object type: {obj?.GetType()}"),
                "UnknownObjectType",
                ErrorCategory.ParserError,
                obj));
        }
    }

    private void EnumerateDisk(VirtualDisk vd, string path, bool recurse, bool namesOnly)
    {
        if (!path.AsSpan().TrimEndAny(Internal.Utilities.PathSeparators).EndsWith("!".AsSpan(), StringComparison.Ordinal))
        {
            path += "!";
        }

        var volMgr = DriveInfo != null ? DriveInfo.VolumeManager : new VolumeManager(vd);
        var volumes = volMgr.GetLogicalVolumes();
        for (var i = 0; i < volumes.Length; ++i)
        {
            var name = $"Volume{i}";
            var volPath = MakePath(path, name);// new PathInfo(PathInfo.Parse(path, true).MountParts, "" + i).ToString();
            WriteItemObject(namesOnly ? name : (object)volumes[i], volPath, true);
            if (recurse)
            {
                GetChildren(volPath, recurse, namesOnly);
            }
        }
    }

    private void EnumerateDirectory(DiscDirectoryInfo parent, string basePath, bool recurse, bool namesOnly)
    {
        foreach (var dir in parent.GetDirectories())
        {
            WriteItemObject(namesOnly ? dir.Name : (object)dir, MakePath(basePath, dir.Name), true);
            if (recurse)
            {
                EnumerateDirectory(dir, MakePath(basePath, dir.Name), recurse, namesOnly);
            }
        }

        foreach (var file in parent.GetFiles())
        {
            WriteItemObject(namesOnly ? file.Name : (object)file, MakePath(basePath, file.Name), false);
        }
    }

    private void DoCopy(DiscDirectoryInfo srcDir, string srcFileName, DiscDirectoryInfo destDir, string destFileName, bool recurse)
    {
        var srcPath = Path.Combine(srcDir.FullName, srcFileName);
        var destPath = Path.Combine(destDir.FullName, destFileName);

        if ((srcDir.FileSystem.GetAttributes(srcPath) & FileAttributes.Directory) == 0)
        {
            DoCopyFile(srcDir.FileSystem, srcPath, destDir.FileSystem, destPath);
        }
        else
        {
            DoCopyDirectory(srcDir.FileSystem, srcPath, destDir.FileSystem, destPath);
            if (recurse)
            {
                DoRecursiveCopy(srcDir.FileSystem, srcPath, destDir.FileSystem, destPath);
            }
        }
    }

    private void DoRecursiveCopy(DiscFileSystem srcFs, string srcPath, DiscFileSystem destFs, string destPath)
    {
        foreach (var dir in srcFs.GetDirectories(srcPath))
        {
            var srcDirPath = Path.Combine(srcPath, dir);
            var destDirPath = Path.Combine(destPath, dir);
            DoCopyDirectory(srcFs, srcDirPath, destFs, destDirPath);
            DoRecursiveCopy(srcFs, srcDirPath, destFs, destDirPath);
        }

        foreach (var file in srcFs.GetFiles(srcPath))
        {
            var srcFilePath = Path.Combine(srcPath, file);
            var destFilePath = Path.Combine(destPath, file);
            DoCopyFile(srcFs, srcFilePath, destFs, destFilePath);
        }
    }

    private void DoCopyDirectory(DiscFileSystem srcFs, string srcPath, DiscFileSystem destFs, string destPath)
    {
        destFs.CreateDirectory(destPath);

        if (srcFs is IWindowsFileSystem srcWindowsFs && destFs is IWindowsFileSystem destWindowsFs)
        {
            if ((srcWindowsFs.GetAttributes(srcPath) & FileAttributes.ReparsePoint) != 0)
            {
                destWindowsFs.SetReparsePoint(destPath, srcWindowsFs.GetReparsePoint(srcPath));
            }

            destWindowsFs.SetSecurity(destPath, srcWindowsFs.GetSecurity(srcPath));
        }

        destFs.SetAttributes(destPath, srcFs.GetAttributes(srcPath));
    }

    private void DoCopyFile(DiscFileSystem srcFs, string srcPath, DiscFileSystem destFs, string destPath)
    {
        using (Stream src = srcFs.OpenFile(srcPath, FileMode.Open, FileAccess.Read))
        using (Stream dest = destFs.OpenFile(destPath, FileMode.Create, FileAccess.ReadWrite))
        {
            dest.SetLength(src.Length);
            var buffer = new byte[1024 * 1024];
            var numRead = src.Read(buffer, 0, buffer.Length);
            while (numRead > 0)
            {
                dest.Write(buffer, 0, numRead);
                numRead = src.Read(buffer, 0, buffer.Length);
            }
        }

        if (srcFs is IWindowsFileSystem srcWindowsFs && destFs is IWindowsFileSystem destWindowsFs)
        {
            if ((srcWindowsFs.GetAttributes(srcPath) & FileAttributes.ReparsePoint) != 0)
            {
                destWindowsFs.SetReparsePoint(destPath, srcWindowsFs.GetReparsePoint(srcPath));
            }

            var sd = srcWindowsFs.GetSecurity(srcPath);
            if(sd != null)
            {
                destWindowsFs.SetSecurity(destPath, sd);
            }
        }

        destFs.SetAttributes(destPath, srcFs.GetAttributes(srcPath));
        destFs.SetCreationTimeUtc(destPath, srcFs.GetCreationTimeUtc(srcPath));
    }
}

