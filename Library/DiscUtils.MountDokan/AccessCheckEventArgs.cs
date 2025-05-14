using DokanNet;
using Microsoft.Win32.SafeHandles;
using System;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CA1021 // Avoid out parameters
#pragma warning disable CA1822 // Mark members as static
#pragma warning disable IDE0057 // Use range operator
#pragma warning disable IDE0022 // Use expression body for methods

namespace DiscUtils.MountDokan;

public class AccessCheckEventArgs : EventArgs
{
    public NtStatus Status { get; set; }
    public string Path { get; internal set; } = null!;
    public bool IsDirectory { get; internal set; }
    public SafeAccessTokenHandle RequestorToken { get; internal set; } = null!;
    public bool SynchronousIo { get; internal set; }
    public bool DeleteOnClose { get; internal set; }
    public bool NoCache { get; internal set; }
    public bool PagingIo { get; internal set; }
    public int ProcessId { get; internal set; }
    public bool WriteToEndOfFile { get; internal set; }
}
