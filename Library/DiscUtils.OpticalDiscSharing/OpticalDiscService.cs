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
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading;
using DiscUtils.Net.Dns;
using LTRData.Extensions.Split;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable SYSLIB0014 // Type or member is obsolete

namespace DiscUtils.OpticalDiscSharing;

/// <summary>
/// Represents a particular Optical Disc Sharing service (typically a Mac or PC).
/// </summary>
public sealed class OpticalDiscService
{
    private string _askToken;
    private ServiceInstance _instance;
    private readonly ServiceDiscoveryClient _sdClient;
    private string _userName;

    internal OpticalDiscService(ServiceInstance instance, ServiceDiscoveryClient sdClient)
    {
        _sdClient = sdClient;
        _instance = instance;
    }

    /// <summary>
    /// Gets information about the optical discs advertised by this service.
    /// </summary>
    public IEnumerable<DiscInfo> AdvertisedDiscs
    {
        get
        {
            foreach (var sdParam in _instance.Parameters)
            {
                if (sdParam.Key.StartsWith("disk"))
                {
                    var diskParams = GetParams(sdParam.Key);

                    var info = new DiscInfo { Name = sdParam.Key };

                    if (diskParams.TryGetValue("adVN", out var infoVal))
                    {
                        info.VolumeLabel = infoVal;
                    }

                    if (diskParams.TryGetValue("adVT", out infoVal))
                    {
                        info.VolumeType = infoVal;
                    }

                    yield return info;
                }
            }
        }
    }

    /// <summary>
    /// Gets the display name of this service.
    /// </summary>
    public string DisplayName => _instance.DisplayName;

    /// <summary>
    /// Connects to the service.
    /// </summary>
    /// <param name="userName">The username to use, if the owner of the Mac / PC is prompted.</param>
    /// <param name="computerName">The computer name to use, if the owner of the Mac / PC is prompted.</param>
    /// <param name="maxWaitSeconds">The maximum number of seconds to wait to be granted access.</param>
    public void Connect(string userName, string computerName, int maxWaitSeconds)
    {
        var sysParams = GetParams("sys");

        var volFlags = 0;
        if (sysParams.TryGetValue("adVF", out var volFlagsStr))
        {
            volFlags = ParseInt(volFlagsStr);
        }

        if ((volFlags & 0x200) != 0)
        {
            _userName = userName;
            AskForAccess(userName, computerName, maxWaitSeconds);

            // Flush any stale mDNS data - the server advertises extra info (such as the discs available)
            // after a client is granted permission to access a disc.
            _sdClient.FlushCache();

            _instance = _sdClient.LookupInstance(_instance.Name, ServiceInstanceFields.All);
        }
    }

    /// <summary>
    /// Opens a shared optical disc as a virtual disk.
    /// </summary>
    /// <param name="name">The name of the disc, from the Name field of DiscInfo.</param>
    /// <returns>The virtual disk.</returns>
    public VirtualDisk OpenDisc(string name)
    {
        var siep = _instance.EndPoints[0];
        var ipAddrs = new List<IPEndPoint>(siep.IPEndPoints);

        var builder = new UriBuilder
        {
            Scheme = "http",
            Host = ipAddrs[0].Address.ToString(),
            Port = ipAddrs[0].Port,
            Path = $"/{name}.dmg"
        };

        return new Disc(builder.Uri, _userName, _askToken);
    }

    private static string GetAskToken(string askId, UriBuilder uriBuilder, int maxWaitSecs)
    {
        uriBuilder.Path = "/ods-ask-status";
        uriBuilder.Query = $"askID={askId}";
        var askStatus = "unknown";
        string askToken = null;

        var start = DateTime.UtcNow;
        var maxWait = TimeSpan.FromSeconds(maxWaitSecs);

        while (askStatus == "unknown" && DateTime.Now - start < maxWait)
        {
            Thread.Sleep(1000);

            var wreq = WebRequest.Create(uriBuilder.Uri);
            wreq.Method = "GET";

            var wrsp = wreq.GetResponse();
            using var inStream = wrsp.GetResponseStream();
            var plist = Plist.Parse(inStream);

            var askBusy = (bool)plist["askBusy"];
            askStatus = plist["askStatus"] as string;

            if (askStatus == "accepted")
            {
                askToken = plist["askToken"] as string;
            }
        }

        if (askToken == null)
        {
            throw new UnauthorizedAccessException("Access not granted");
        }

        return askToken;
    }

    private static string InitiateAsk(string userName, string computerName, UriBuilder uriBuilder)
    {
        uriBuilder.Path = "/ods-ask";

        var wreq = (HttpWebRequest)WebRequest.Create(uriBuilder.Uri);
        wreq.Method = "POST";

        var req = new Dictionary<string, object>
        {
            ["askDevice"] = string.Empty,
            ["computer"] = computerName,
            ["user"] = userName
        };

        using (var outStream = wreq.GetRequestStream())
        {
            Plist.Write(outStream, req);
        }

        string askId;
        var wrsp = wreq.GetResponse();
        using (var inStream = wrsp.GetResponseStream())
        {
            var plist = Plist.Parse(inStream);
            askId = ((int)plist["askID"]).ToString(CultureInfo.InvariantCulture);
        }

        return askId;
    }

    private static int ParseInt(string volFlagsStr)
    {
        if (volFlagsStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
            return int.Parse(volFlagsStr.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
#else
            return int.Parse(volFlagsStr.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
#endif
        }

        return int.Parse(volFlagsStr, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private void AskForAccess(string userName, string computerName, int maxWaitSecs)
    {
        var siep = _instance.EndPoints[0];
        var ipAddrs = new List<IPEndPoint>(siep.IPEndPoints);

        var uriBuilder = new UriBuilder
        {
            Scheme = "http",
            Host = ipAddrs[0].Address.ToString(),
            Port = ipAddrs[0].Port
        };

        var askId = InitiateAsk(userName, computerName, uriBuilder);

        _askToken = GetAskToken(askId, uriBuilder, maxWaitSecs);
    }

    private Dictionary<string, string> GetParams(string section)
    {
        var result = new Dictionary<string, string>();

        if (_instance.Parameters.TryGetValue(section, out var data))
        {
            var asString = Encoding.ASCII.GetString(data);
            var nvPairs = asString.AsSpan().TokenEnum(',');

            foreach (var nvPair in nvPairs)
            {
                var parts = nvPair.TokenEnum('=');
                result[parts.First().ToString()] = parts.ElementAt(1).ToString();
            }
        }

        return result;
    }
}