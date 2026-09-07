namespace DiscUtils.FileSystems;

public static class SetupHelper
{
    /// <summary>Registers the file-system package's providers without reflection.</summary>
    public static void SetupFileSystems()
    {
        Core.Formats.Register();
        Btrfs.Formats.Register();
        Ext.Formats.Register();
        Fat.Formats.Register();
        ExFat.Formats.Register();
        HfsPlus.Formats.Register();
        Ntfs.Formats.Register();
        OpticalDisk.Formats.Register();
        SquashFs.Formats.Register();
        Swap.Formats.Register();
        Xfs.Formats.Register();
        VirtualFileSystem.Formats.Register();
    }
}
