using DiscUtils.Btrfs;
using DiscUtils.BootConfig;
using DiscUtils.Dmg;
using DiscUtils.ExFat;
using DiscUtils.Ext;
using DiscUtils.Fat;
using DiscUtils.HfsPlus;
using DiscUtils.Iso9660;
using DiscUtils.Lvm;
using DiscUtils.Nfs;
using DiscUtils.Ntfs;
using DiscUtils.OpticalDisk;
using DiscUtils.Registry;
using DiscUtils.Sdi;
using DiscUtils.SquashFs;
using DiscUtils.Udf;
using DiscUtils.Wim;
using DiscUtils.Xfs;
using DiscUtils.Net.Dns;
using DiscUtils.OpticalDiscSharing;

namespace DiscUtils.Complete;

public static class SetupHelper
{
 public static void SetupComplete()
    {
        Setup.SetupHelper.RegisterAssembly(typeof(Store).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(Disk).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(BtrfsFileSystem).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(ExtFileSystem).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(FatFileSystem).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(ExFatFileSystem).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(HfsPlusFileSystem).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(Iscsi.Disk).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(BuildFileInfo).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(DnsClient).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(Nfs3Status).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(NtfsFileSystem).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(DiscInfo).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(Disc).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(RegistryHive).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(SdiFile).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(SquashFileSystemBuilder).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(Swap.SwapFileSystem).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(UdfReader).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(Vdi.Disk).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(Vhd.Disk).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(Vhdx.Disk).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(Vmdk.Disk).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(VirtualFileSystem.VirtualFileSystem).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(WimFile).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(XfsFileSystem).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(Xva.Disk).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(Lvm.LogicalVolumeManager).Assembly);
    }
    public static void SetupCompleteAot()
    {
        DiscUtils.Setup.GeneratedSetupHelper.RegisterFactories();
    }
}