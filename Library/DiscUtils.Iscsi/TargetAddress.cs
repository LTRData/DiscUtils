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
using System.Globalization;

namespace DiscUtils.Iscsi;

/// <summary>
/// Information about an iSCSI Target.
/// </summary>
/// <remarks>
/// A target contains zero or more LUNs.
/// </remarks>
public class TargetAddress
{
    internal const int DefaultPort = 3260;

    /// <summary>
    /// Initializes a new instance of the TargetAddress class.
    /// </summary>
    /// <param name="address">The IP address (or FQDN) of the Target.</param>
    /// <param name="port">The network port of the Target.</param>
    /// <param name="targetGroupTag">The Group Tag of the Target.</param>
    public TargetAddress(string address, int port, string targetGroupTag)
    {
        NetworkAddress = address;
        NetworkPort = port;
        TargetGroupTag = targetGroupTag;
    }

    /// <summary>
    /// Gets the IP address (or FQDN) of the Target.
    /// </summary>
    public string NetworkAddress { get; }

    /// <summary>
    /// Gets the network port of the Target.
    /// </summary>
    public int NetworkPort { get; }

    /// <summary>
    /// Gets the Group Tag of the Target.
    /// </summary>
    public string TargetGroupTag { get; }

    /// <summary>
    /// Parses a Target address in string form.
    /// </summary>
    /// <param name="address">The address to parse.</param>
    /// <returns>The structured address.</returns>
    public static TargetAddress Parse(string address)
    {
        var addrEnd = address.AsSpan().IndexOfAny(':', ',');
        if (addrEnd == -1)
        {
            return new TargetAddress(address, DefaultPort, string.Empty);
        }

        var addr = address[..addrEnd];
        var port = DefaultPort;
        var targetGroupTag = string.Empty;

        var focus = addrEnd;
        if (address[focus] == ':')
        {
            var portStart = addrEnd + 1;
            var portEnd = address.IndexOf(',', portStart);

            if (portEnd == -1)
            {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
                port = int.Parse(address.AsSpan(portStart), provider: CultureInfo.InvariantCulture);
#else
                port = int.Parse(address[portStart..], CultureInfo.InvariantCulture);
#endif
                focus = address.Length;
            }
            else
            {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
                port = int.Parse(address.AsSpan(portStart, portEnd - portStart), provider: CultureInfo.InvariantCulture);
#else
                port = int.Parse(address[portStart..portEnd], CultureInfo.InvariantCulture);
#endif
                focus = portEnd;
            }
        }

        if (focus < address.Length)
        {
            targetGroupTag = address[(focus + 1)..];
        }

        return new TargetAddress(addr, port, targetGroupTag);
    }

    /// <summary>
    /// Gets the TargetAddress in string format.
    /// </summary>
    /// <returns>The string in 'host:port,targetgroup' format.</returns>
    public override string ToString()
    {
        var result = NetworkAddress;
        if (NetworkPort != DefaultPort)
        {
            result += $":{NetworkPort}";
        }

        if (!string.IsNullOrEmpty(TargetGroupTag))
        {
            result += $",{TargetGroupTag}";
        }

        return result;
    }

    /// <summary>
    /// Gets the target address as a URI.
    /// </summary>
    /// <returns>The target address in the form: iscsi://host[:port][/grouptag].</returns>
    public Uri ToUri()
    {
        var builder = new UriBuilder
        {
            Scheme = "iscsi",
            Host = NetworkAddress,
            Port = NetworkPort != DefaultPort ? NetworkPort : -1,
            Path = string.IsNullOrEmpty(TargetGroupTag) ? string.Empty : TargetGroupTag
        };
        return builder.Uri;
    }
}