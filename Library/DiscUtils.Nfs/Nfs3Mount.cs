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

using System.Collections.Generic;
using System.IO;

namespace DiscUtils.Nfs;

// For more information, see
// https://www.ietf.org/rfc/rfc1813.txt Appendix I: Mount Protocol
internal sealed class Nfs3Mount : RpcProgram
{
    public const int ProgramIdentifier = RpcIdentifiers.Nfs3MountProgramIdentifier;
    public const int ProgramVersion = RpcIdentifiers.Nfs3MountProgramVersion;

    public const int MaxPathLength = 1024;
    public const int MaxNameLength = 255;
    public const int MaxFileHandleSize = 64;

    public Nfs3Mount(IRpcClient client)
        : base(client) { }

    public override int Identifier => ProgramIdentifier;

    public override int Version => ProgramVersion;

    public IEnumerable<Nfs3Export> Exports()
    {
        var ms = new MemoryStream();
        var writer = StartCallMessage(ms, null, MountProc3.Export);

        var reply = DoSend(ms);
        if (reply.Header.IsSuccess)
        {
            while (reply.BodyReader.ReadBool())
            {
                yield return new Nfs3Export(reply.BodyReader);
            }

            yield break;
        }

        throw new RpcException(reply.Header.ReplyHeader);
    }

    public Nfs3MountResult Mount(string dirPath)
    {
        var ms = new MemoryStream();
        var writer = StartCallMessage(ms, _client.Credentials, MountProc3.Mnt);
        writer.Write(dirPath);

        var reply = DoSend(ms);
        if (reply.Header.IsSuccess)
        {
            return new Nfs3MountResult(reply.BodyReader);
        }

        throw new RpcException(reply.Header.ReplyHeader);
    }
}
