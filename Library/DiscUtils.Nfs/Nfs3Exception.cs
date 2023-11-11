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
using System.Runtime.Serialization;
using System.Security.Permissions;

namespace DiscUtils.Nfs;

/// <summary>
/// Exception thrown when some invalid file system data is found, indicating probably corruption.
/// </summary>
[Serializable]
public sealed class Nfs3Exception : IOException
{
    /// <summary>
    /// Initializes a new instance of the Nfs3Exception class.
    /// </summary>
    public Nfs3Exception() {}

    /// <summary>
    /// Initializes a new instance of the Nfs3Exception class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public Nfs3Exception(string message)
        : base(message) {}

    /// <summary>
    /// Initializes a new instance of the Nfs3Exception class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="status">The status result of an NFS procedure.</param>
    public Nfs3Exception(string message, Nfs3Status status)
        : base(message)
    {
        NfsStatus = status;
    }

    /// <summary>
    /// Initializes a new instance of the Nfs3Exception class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public Nfs3Exception(string message, Exception innerException)
        : base(message, innerException) {}

    /// <summary>
    /// Initializes a new instance of the Nfs3Exception class.
    /// </summary>
    /// <param name="status">The status result of an NFS procedure.</param>
    internal Nfs3Exception(Nfs3Status status)
        : base(GenerateMessage(status))
    {
        NfsStatus = status;
    }

    /// <summary>
    /// Initializes a new instance of the Nfs3Exception class.
    /// </summary>
    /// <param name="info">The serialization info.</param>
    /// <param name="context">The streaming context.</param>
#if !NETCOREAPP
    [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.SerializationFormatter)]
#elif NET8_0 || NET8_0_OR_GREATER
    [Obsolete]
#endif
    private Nfs3Exception(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        NfsStatus = (Nfs3Status)info.GetInt32("Status");
    }

    /// <summary>
    /// Gets the NFS status code that lead to the exception.
    /// </summary>
    public Nfs3Status NfsStatus { get; } = Nfs3Status.Unknown;

    /// <summary>
    /// Serializes this exception.
    /// </summary>
    /// <param name="info">The object to populate with serialized data.</param>
    /// <param name="context">The context for this serialization.</param>
#if !NETCOREAPP
    [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.SerializationFormatter)]
#elif NET8_0 || NET8_0_OR_GREATER
    [Obsolete]
#endif
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        info.AddValue("Status", (int)NfsStatus);
        base.GetObjectData(info, context);
    }

    private static string GenerateMessage(Nfs3Status status)
    {
        return status switch
        {
            Nfs3Status.Ok => "OK",
            Nfs3Status.NotOwner => "Not owner",
            Nfs3Status.NoSuchEntity => "No such file or directory",
            Nfs3Status.IOError => "Hardware I/O error",
            Nfs3Status.NoSuchDeviceOrAddress => "I/O error - no such device or address",
            Nfs3Status.AccessDenied => "Permission denied",
            Nfs3Status.FileExists => "File exists",
            Nfs3Status.AttemptedCrossDeviceHardLink => "Attempted cross-device hard link",
            Nfs3Status.NoSuchDevice => "No such device",
            Nfs3Status.NotDirectory => "Not a directory",
            Nfs3Status.IsADirectory => "Is a directory",
            Nfs3Status.InvalidArgument => "Invalid or unsupported argument",
            Nfs3Status.FileTooLarge => "File too large",
            Nfs3Status.NoSpaceAvailable => "No space left on device",
            Nfs3Status.ReadOnlyFileSystem => "Read-only file system",
            Nfs3Status.TooManyHardLinks => "Too many hard links",
            Nfs3Status.NameTooLong => "Name too long",
            Nfs3Status.DirectoryNotEmpty => "Directory not empty",
            Nfs3Status.QuotaHardLimitExceeded => "Quota hard limit exceeded",
            Nfs3Status.StaleFileHandle => "Invalid (stale) file handle",
            Nfs3Status.TooManyRemoteAccessLevels => "Too many levels of remote access",
            Nfs3Status.BadFileHandle => "Illegal NFS file handle",
            Nfs3Status.UpdateSynchronizationError => "Update synchronization error",
            Nfs3Status.StaleCookie => "Read directory cookie stale",
            Nfs3Status.NotSupported => "Operation is not supported",
            Nfs3Status.TooSmall => "Buffer or request is too small",
            Nfs3Status.ServerFault => "Server fault",
            Nfs3Status.BadType => "Server doesn't support object type",
            Nfs3Status.SlowJukebox => "Unable to complete in timely fashion",
            _ => $"Unknown error: {status}",
        };
    }
}