using DiscUtils.Btrfs;
using DiscUtils.ExFat;
using DiscUtils.Ext;
using DiscUtils.Fat;
using DiscUtils.HfsPlus;
using DiscUtils.Ntfs;
using DiscUtils.OpticalDisk;
using DiscUtils.SquashFs;
using DiscUtils.Swap;
using DiscUtils.Xfs;

namespace DiscUtils.FileSystems;

public static class SetupHelper
{
    public static void SetupFileSystems()
    {
        Setup.SetupHelper.RegisterAssembly(typeof(BtrfsFileSystem).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(ExtFileSystem).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(FatFileSystem).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(ExFatFileSystem).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(HfsPlusFileSystem).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(NtfsFileSystem).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(Disc).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(SquashFileSystemBuilder).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(SwapFileSystem).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(XfsFileSystem).Assembly);
        Setup.SetupHelper.RegisterAssembly(typeof(VirtualFileSystem.VirtualFileSystem).Assembly);
    }
}