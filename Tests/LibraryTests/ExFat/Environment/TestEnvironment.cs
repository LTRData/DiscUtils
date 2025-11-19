// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

// Validation with Windows chkdsk is disabled for now.
// There are issues with finding the correct volume to
// check as well as actual file system issues that
// make chkdsk stop with "an unspecified error occurred"
// in the test cases where the entire partition and file
// system is created by the test case.
//
// This needs more investigation before it can be
// activated again.
// // Olof, LTRData
//
//#define CHKDSK_VALIDATION

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Threading;
using DiscUtils.Vhdx;

namespace LibraryTests.ExFat.Environment;

internal class TestEnvironment : IDisposable
{
    protected string vhdxPath = null!;
    protected Guid volumeId;
    protected Disk? disk;

    protected TestEnvironment()
    {
    }

#if NETCOREAPP
    [SupportedOSPlatform("windows")]
#endif
    private static bool IsAdmin
    {
        get
        {
            using var id = WindowsIdentity.GetCurrent();
            return id.Groups!.Contains(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        }
    }

    public virtual void Dispose()
    {
        disk?.Dispose();
        // a check when required
        if (vhdxPath != null && File.Exists(vhdxPath))
        {
            try
            {
#if NET461_OR_GREATER || NETSTANDARD || NETCOREAPP
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Trace.WriteLine("Validation with chkdsk requires Windows.");
                    return;
                }
#endif

#if CHKDSK_VALIDATION
                if (IsAdmin)
                {
                    var t = CheckDisk();
                    if (!t.Item1)
                    {
                        throw new Exception($"VHDX filesystem is found corrupted by CHKDSK: {t.Item2}");
                    }
                }
                else
                {
                    Trace.WriteLine("Validation with chkdsk requires administrative privileges.");
                }
#endif
            }
            finally
            {
                File.Delete(vhdxPath);
            }
        }
    }

#if NET9_0_OR_GREATER
    private static readonly Lock _lock = new();
#else
    private static readonly object _lock = new();
#endif

#if NETCOREAPP
    [SupportedOSPlatform("windows")]
#endif
    private (bool success, string? checkResult) CheckDisk()
    {
        lock (_lock)
        {
            RunDiskPartAttach(vhdxPath);
            
            var result = ProcessUtility.Run("chkdsk", @$"\\?\Volume{{{volumeId}}} /x");
            var success = result.exitCode == 0;
            var checkResult = result.result;

            RunDiskPartDetach(vhdxPath);

            return (success, checkResult);
        }
    }

#if NETCOREAPP
    [SupportedOSPlatform("windows")]
#endif
    private static void RunDiskPartAttach(string vdiskPath)
    {
        var scriptPath = Path.GetTempFileName();
        using (var scriptStream = File.CreateText(scriptPath))
        {
            scriptStream.WriteLine($"select vdisk file=\"{vdiskPath}\"");
            scriptStream.WriteLine("attach vdisk");
            scriptStream.WriteLine("online disk");
            scriptStream.WriteLine("select partition 2");
            scriptStream.WriteLine("online volume");
        }

        ProcessUtility.Run("diskpart", $"/s {scriptPath}");
        File.Delete(scriptPath);
    }

#if NETCOREAPP
    [SupportedOSPlatform("windows")]
#endif
    private static void RunDiskPartDetach(string vdiskPath)
    {
        var scriptPath = Path.GetTempFileName();
        using (var scriptStream = File.CreateText(scriptPath))
        {
            scriptStream.WriteLine($"select vdisk file=\"{vdiskPath}\"");
            scriptStream.WriteLine($"detach vdisk");
        }

        ProcessUtility.Run("diskpart", $"/s {scriptPath}");
        File.Delete(scriptPath);
    }
}