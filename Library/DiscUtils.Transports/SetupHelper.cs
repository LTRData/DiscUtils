namespace DiscUtils.Transports;

public static class SetupHelper
{
    /// <summary>Registers the transport package's providers without reflection.</summary>
    public static void SetupTransports()
    {
        Core.Formats.Register();
        Iscsi.Formats.Register();
        Nfs.Formats.Register();
        OpticalDisk.Formats.Register();
    }
}
