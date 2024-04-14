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
using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DiscUtils.Iscsi;

internal class ChapAuthenticator : Authenticator
{
    private int _algorithm;
    private byte[] _challenge;
    private byte _identifier;

    private readonly string _name;
    private readonly string _password;
    private State _state;

    public ChapAuthenticator(string name, string password)
    {
        _name = name;
        _password = password;
        _state = State.SendAlgorithm;
    }

    public override string Identifier => "CHAP";

    public override bool GetParameters(TextBuffer textBuffer)
    {
        switch (_state)
        {
            case State.SendAlgorithm:
                textBuffer.Add("CHAP_A", "5");
                _state = State.ReceiveChallenge;
                return false;
            case State.SendResponse:
                textBuffer.Add("CHAP_N", _name);
                textBuffer.Add("CHAP_R", CalcResponse());
                _state = State.Finished;
                return true;
            default:
                throw new InvalidOperationException($"Unknown authentication state: {_state}");
        }
    }

    public override void SetParameters(TextBuffer textBuffer)
    {
        switch (_state)
        {
            case State.ReceiveChallenge:
                _algorithm = int.Parse(textBuffer["CHAP_A"], CultureInfo.InvariantCulture);
                _identifier = byte.Parse(textBuffer["CHAP_I"], CultureInfo.InvariantCulture);
                _challenge = ParseByteString(textBuffer["CHAP_C"]);
                _state = State.SendResponse;

                if (_algorithm != 0x5)
                {
                    throw new LoginException($"Unexpected CHAP authentication algorithm: {_algorithm}");
                }

                return;
            default:
                throw new InvalidOperationException($"Unknown authentication state: {_state}");
        }
    }

    private static byte[] ParseByteString(string p)
    {
        if (!p.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidProtocolException("Invalid value in CHAP exchange");
        }

        var data = new byte[(p.Length - 2) / 2];
        for (var i = 0; i < data.Length; ++i)
        {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
            data[i] = byte.Parse(p.AsSpan(2 + i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
#else
            data[i] = byte.Parse(p.Substring(2 + i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
#endif
        }

        return data;
    }

#if NET5_0_OR_GREATER

    private static byte[] CalcMD5Hash(byte[] bytes, int offset, int count) => MD5.HashData(bytes.AsSpan(offset, count));

#else

    [ThreadStatic]
    private static MD5 _md5;

    private static byte[] CalcMD5Hash(byte[] bytes, int offset, int count)
    {
        _md5 ??= MD5.Create();
        return _md5.ComputeHash(bytes, offset, count);
    }

#endif

    private string CalcResponse()
    {
        var toHashLength = 1 + _password.Length + _challenge.Length;
        var toHash = ArrayPool<byte>.Shared.Rent(toHashLength);
        try
        {
            toHash[0] = _identifier;
            Encoding.ASCII.GetBytes(_password, 0, _password.Length, toHash, 1);
            Buffer.BlockCopy(_challenge, 0, toHash, _password.Length + 1, _challenge.Length);

            var hash = CalcMD5Hash(toHash, 0, toHashLength);

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
            var result = string.Create(2 + hash.Length * 2, hash,
                (span, bytes) =>
                {
                    "0x".AsSpan().CopyTo(span);
                    for (var i = 0; i < hash.Length; ++i)
                    {
                        span = span.Slice(2);
                        hash[i].TryFormat(span, out _, "x2");
                    }
                });

            return result;
#else
            var result = new StringBuilder("0x");
            for (var i = 0; i < hash.Length; ++i)
            {
                result.AppendFormat("x2", hash[i]);
            }

            return result.ToString();
#endif
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(toHash);
        }
    }

    private enum State
    {
        SendAlgorithm,
        ReceiveChallenge,
        SendResponse,
        Finished
    }
}