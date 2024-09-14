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
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;
using LTRData.Extensions.Buffers;
using LTRData.Extensions.Split;
using Buffer = DiscUtils.Streams.Buffer;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable SYSLIB0014 // Type or member is obsolete

namespace DiscUtils.OpticalDiscSharing;

internal sealed class DiscContentBuffer : Buffer
{
    private string _authHeader;

    private readonly string _password;
    private readonly Uri _uri;
    private readonly string _userName;

    internal DiscContentBuffer(Uri uri, string userName, string password)
    {
        _uri = uri;
        _userName = userName;
        _password = password;

        var response = SendRequest(() =>
        {
            var wr = (HttpWebRequest)WebRequest.Create(uri);
            wr.Method = "HEAD";
            return wr;
        });

        Capacity = response.ContentLength;
    }

    public override bool CanRead => true;

    public override bool CanWrite => false;

    public override long Capacity { get; }

    public override int Read(long pos, byte[] buffer, int offset, int count)
    {
        var response = SendRequest(() =>
        {
            var wr = (HttpWebRequest)WebRequest.Create(_uri);
            wr.Method = "GET";
            wr.AddRange((int)pos, (int)(pos + count - 1));
            return wr;
        });

        using var s = response.GetResponseStream();
        var total = (int)response.ContentLength;
        var read = 0;
        while (read < Math.Min(total, count))
        {
            read += s.Read(buffer, offset + read, count - read);
        }

        return read;
    }

    public override async ValueTask<int> ReadAsync(long pos, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(() =>
        {
            var wr = (HttpWebRequest)WebRequest.Create(_uri);
            wr.Method = "GET";
            wr.AddRange((int)pos, (int)(pos + buffer.Length - 1));
            return wr;
        }).ConfigureAwait(false);

        using var s = response.GetResponseStream();
        var total = (int)response.ContentLength;
        var read = 0;
        while (read < Math.Min(total, buffer.Length))
        {
            read += await s.ReadAsync(buffer.Slice(read), cancellationToken).ConfigureAwait(false);
        }

        return read;
    }

    public override int Read(long pos, Span<byte> buffer)
    {
        var count = buffer.Length;

        var response = SendRequest(() =>
        {
            var wr = (HttpWebRequest)WebRequest.Create(_uri);
            wr.Method = "GET";
            wr.AddRange((int)pos, (int)(pos + count - 1));
            return wr;
        });

        using var s = response.GetResponseStream();
        var total = (int)response.ContentLength;
        var read = 0;
        while (read < Math.Min(total, buffer.Length))
        {
            read += s.Read(buffer.Slice(read));
        }

        return read;
    }

    public override void Write(long pos, byte[] buffer, int offset, int count)
    {
        throw new InvalidOperationException("Attempt to write to shared optical disc");
    }

    public override void Write(long pos, ReadOnlySpan<byte> buffer) =>
        throw new InvalidOperationException("Attempt to write to shared optical disc");

    public override void SetCapacity(long value)
    {
        throw new InvalidOperationException("Attempt to change size of shared optical disc");
    }

    public override IEnumerable<StreamExtent> GetExtentsInRange(long start, long count)
    {
        return StreamExtent.Intersect(
            SingleValueEnumerable.Get(new StreamExtent(0, Capacity)),
            new StreamExtent(start, count));
    }

    private static string ToHexString(byte[] p)
    {
        var result = new StringBuilder();

        for (var i = 0; i < p.Length; ++i)
        {
            var j = (p[i] >> 4) & 0xf;
            result.Append((char)(j <= 9 ? '0' + j : 'a' + (j - 10)));
            j = p[i] & 0xf;
            result.Append((char)(j <= 9 ? '0' + j : 'a' + (j - 10)));
        }

        return result.ToString();
    }

    private static Dictionary<string, string> ParseAuthenticationHeader(string header, out string authMethod)
    {
        var result = new Dictionary<string, string>();

        var elements = header.AsMemory().TokenEnum(' ').ToArray();

        authMethod = elements[0].ToString();

        for (var i = 1; i < elements.Length; ++i)
        {
            var nvPair = elements[i].TokenEnum('=', StringSplitOptions.None);
            result.Add(nvPair.ElementAt(0).ToString(), nvPair.ElementAt(1).Span.Trim('\"').ToString());
        }

        return result;
    }

    private HttpWebResponse SendRequest(WebRequestCreator wrc)
    {
        var wr = wrc();
        if (_authHeader != null)
        {
            wr.Headers["Authorization"] = _authHeader;
        }

        try
        {
            return (HttpWebResponse)wr.GetResponse();
        }
        catch (WebException we)
        {
            var wresp = (HttpWebResponse)we.Response;

            if (wresp.StatusCode == HttpStatusCode.Unauthorized)
            {
                var authParams = ParseAuthenticationHeader(wresp.Headers["WWW-Authenticate"], out var authMethod);

                if (authMethod != "Digest")
                {
                    throw;
                }

                var resp = CalcDigestResponse(authParams["nonce"], wr.RequestUri.AbsolutePath, wr.Method, authParams["realm"]);

                _authHeader = $"Digest username=\"{_userName}\", realm=\"ODS\", nonce=\"{authParams["nonce"]}\", uri=\"{wr.RequestUri.AbsolutePath}\", response=\"{resp}\"";

                (wresp as IDisposable).Dispose();

                wr = wrc();
                wr.Headers["Authorization"] = _authHeader;

                return (HttpWebResponse)wr.GetResponse();
            }

            throw;
        }
    }

    private async Task<HttpWebResponse> SendRequestAsync(WebRequestCreator wrc)
    {
        var wr = wrc();
        if (_authHeader != null)
        {
            wr.Headers["Authorization"] = _authHeader;
        }

        try
        {
            return (HttpWebResponse)await wr.GetResponseAsync().ConfigureAwait(false);
        }
        catch (WebException we)
        {
            var wresp = (HttpWebResponse)we.Response;

            if (wresp.StatusCode == HttpStatusCode.Unauthorized)
            {
                var authParams = ParseAuthenticationHeader(wresp.Headers["WWW-Authenticate"], out var authMethod);

                if (authMethod != "Digest")
                {
                    throw;
                }

                var resp = CalcDigestResponse(authParams["nonce"], wr.RequestUri.AbsolutePath, wr.Method, authParams["realm"]);

                _authHeader = $"Digest username=\"{_userName}\", realm=\"ODS\", nonce=\"{authParams["nonce"]}\", uri=\"{wr.RequestUri.AbsolutePath}\", response=\"{resp}\"";

                (wresp as IDisposable).Dispose();

                wr = wrc();
                wr.Headers["Authorization"] = _authHeader;

                return (HttpWebResponse)await wr.GetResponseAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

#if NET5_0_OR_GREATER

    private static byte[] CalcMD5Hash(byte[] bytes) => MD5.HashData(bytes);

#else

    [ThreadStatic]
    private static MD5 _md5;

    private static byte[] CalcMD5Hash(byte[] bytes)
    {
        _md5 ??= MD5.Create();
        return _md5.ComputeHash(bytes);
    }

#endif

    private string CalcDigestResponse(string nonce, string uriPath, string method, string realm)
    {
        var a2 = $"{method}:{uriPath}";
        var ha2 = ToHexString(CalcMD5Hash(Encoding.ASCII.GetBytes(a2)));

        var a1 = $"{_userName}:{realm}:{_password}";
        var ha1 = ToHexString(CalcMD5Hash(Encoding.ASCII.GetBytes(a1)));

        var toHash = $"{ha1}:{nonce}:{ha2}";
        var hash = CalcMD5Hash(Encoding.ASCII.GetBytes(toHash));
        return ToHexString(hash);
    }

    internal delegate HttpWebRequest WebRequestCreator();
}