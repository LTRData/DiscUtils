namespace DiscUtils.Complete;

public static class SetupHelper
{
    /// <summary>Registers all built-in formats and providers without reflection.</summary>
    public static void SetupComplete()
    {
        Core.Formats.Register();
        Dmg.Formats.Register();
        Btrfs.Formats.Register();
        Ext.Formats.Register();
        Fat.Formats.Register();
        ExFat.Formats.Register();
        HfsPlus.Formats.Register();
        Iscsi.Formats.Register();
        Nfs.Formats.Register();
        Ntfs.Formats.Register();
        OpticalDiscSharing.Formats.Register();
        OpticalDisk.Formats.Register();
        SquashFs.Formats.Register();
        Swap.Formats.Register();
        Vdi.Formats.Register();
        Vhd.Formats.Register();
        Vhdx.Formats.Register();
        Vmdk.Formats.Register();
        VirtualFileSystem.Formats.Register();
        Xfs.Formats.Register();
        Xva.Formats.Register();
        Lvm.Formats.Register();
    }
}
