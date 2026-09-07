namespace DiscUtils.Containers;

public static class SetupHelper
{
    /// <summary>Registers the container package's disk and volume providers without reflection.</summary>
    public static void SetupContainers()
    {
        Core.Formats.Register();
        Dmg.Formats.Register();
        Lvm.Formats.Register();
        Vhd.Formats.Register();
        Vhdx.Formats.Register();
        Vmdk.Formats.Register();
        Vdi.Formats.Register();
        Xva.Formats.Register();
    }
}
