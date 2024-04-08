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
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;
using LTRData.Extensions.Buffers;

namespace DiscUtils.Iscsi;

internal sealed class Connection : IDisposable
{
    private readonly Authenticator[] _authenticators;

    /// <summary>
    /// The set of all 'parameters' we've negotiated.
    /// </summary>
    private readonly Dictionary<string, string> _negotiatedParameters;

    private readonly NetworkStream _stream;

    public Connection(Session session, TargetAddress address, Authenticator[] authenticators)
    {
        Session = session;
        _authenticators = authenticators;

        var client = new TcpClient(address.NetworkAddress, address.NetworkPort)
        {
            NoDelay = true
        };
        _stream = client.GetStream();

        Id = session.NextConnectionId();

        // Default negotiated values
        HeaderDigest = Digest.None;
        DataDigest = Digest.None;
        MaxInitiatorTransmitDataSegmentLength = 131072;
        MaxTargetReceiveDataSegmentLength = 8192;

        _negotiatedParameters = new Dictionary<string, string>();
        NegotiateSecurity();
        NegotiateFeatures();
    }

    internal LoginStages CurrentLoginStage { get; private set; } = LoginStages.SecurityNegotiation;

    internal uint ExpectedStatusSequenceNumber { get; private set; } = 1;

    internal ushort Id { get; }

    internal LoginStages NextLoginStage
    {
        get
        {
            return CurrentLoginStage switch
            {
                LoginStages.SecurityNegotiation => LoginStages.LoginOperationalNegotiation,
                LoginStages.LoginOperationalNegotiation => LoginStages.FullFeaturePhase,
                _ => LoginStages.FullFeaturePhase,
            };
        }
    }

    internal Session Session { get; }

    public void Dispose()
    {
        Close(LogoutReason.CloseConnection);
    }

    public void Close(LogoutReason reason)
    {
        var req = new LogoutRequest(this);
        var packet = req.GetBytes(reason);
        _stream.Write(packet, 0, packet.Length);
        _stream.Flush();

        var pdu = ReadPdu();
        var resp = ParseResponse<LogoutResponse>(pdu);

        if (resp.Response != LogoutResponseCode.ClosedSuccessfully)
        {
            throw new InvalidProtocolException($"Target indicated failure during logout: {resp.Response}");
        }

        _stream.Dispose();
    }

    /// <summary>
    /// Sends an SCSI command (aka task) to a LUN via the connected target.
    /// </summary>
    /// <param name="cmd">The command to send.</param>
    /// <param name="outBuffer">The data to send with the command.</param>
    /// <param name="inBuffer">The buffer to fill with returned data.</param>
    /// <returns>The number of bytes received.</returns>
    public int Send(ScsiCommand cmd, ReadOnlySpan<byte> outBuffer, Span<byte> inBuffer)
    {
        var req = new CommandRequest(this, cmd.TargetLun);

        var outBufferCount = outBuffer.Length;
        var inBufferMax = inBuffer.Length;

        var toSend = Math.Min(Math.Min(outBufferCount, Session.ImmediateData ? Session.FirstBurstLength : 0), MaxTargetReceiveDataSegmentLength);
        var packet = req.GetBytes(cmd, outBuffer.Slice(0, toSend), true, inBufferMax != 0, outBufferCount != 0, (uint)(outBufferCount != 0 ? outBufferCount : inBufferMax));
        _stream.Write(packet, 0, packet.Length);
        _stream.Flush();
        var numSent = toSend;
        var pktsSent = 0;
        while (numSent < outBufferCount)
        {
            var pdu = ReadPdu();

            var resp = ParseResponse<ReadyToTransferPacket>(pdu);
            var numApproved = (int)resp.DesiredTransferLength;
            var targetTransferTag = resp.TargetTransferTag;

            while (numApproved > 0)
            {
                toSend = Math.Min(Math.Min(outBufferCount - numSent, numApproved), MaxTargetReceiveDataSegmentLength);

                var pkt = new DataOutPacket(this, cmd.TargetLun);
                packet = pkt.GetBytes(outBuffer.Slice(numSent, toSend), toSend == numApproved, pktsSent++, (uint)numSent, targetTransferTag);
                _stream.Write(packet, 0, packet.Length);
                _stream.Flush();

                numApproved -= toSend;
                numSent += toSend;
            }
        }

        var isFinal = false;
        var numRead = 0;
        while (!isFinal)
        {
            var pdu = ReadPdu();

            if (pdu.OpCode == OpCode.ScsiResponse)
            {
                var resp = ParseResponse<Response>(pdu);

                if (resp.StatusPresent && resp.Status == ScsiStatus.CheckCondition)
                {
                    var senseLength = EndianUtilities.ToUInt16BigEndian(pdu.ContentData, 0);
                    var senseData = new byte[senseLength];
                    System.Buffer.BlockCopy(pdu.ContentData, 2, senseData, 0, senseLength);
                    throw new ScsiCommandException(resp.Status, "Target indicated SCSI failure", senseData);
                }

                if (resp.StatusPresent && resp.Status != ScsiStatus.Good)
                {
                    throw new ScsiCommandException(resp.Status, "Target indicated SCSI failure");
                }

                isFinal = resp.Header.FinalPdu;
            }
            else if (pdu.OpCode == OpCode.ScsiDataIn)
            {
                var resp = ParseResponse<DataInPacket>(pdu);

                if (resp.StatusPresent && resp.Status != ScsiStatus.Good)
                {
                    throw new ScsiCommandException(resp.Status, "Target indicated SCSI failure");
                }

                if (resp.ReadData != null)
                {
                    resp.ReadData.AsSpan().CopyTo(inBuffer.Slice((int)resp.BufferOffset));
                    numRead += resp.ReadData.Length;
                }

                isFinal = resp.Header.FinalPdu;
            }
        }

        Session.NextTaskTag();
        Session.NextCommandSequenceNumber();

        return numRead;
    }

    /// <summary>
    /// Sends an SCSI command (aka task) to a LUN via the connected target.
    /// </summary>
    /// <param name="cmd">The command to send.</param>
    /// <param name="outBuffer">The data to send with the command.</param>
    /// <param name="inBuffer">The buffer to fill with returned data.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The number of bytes received.</returns>
    public async ValueTask<int> SendAsync(ScsiCommand cmd, ReadOnlyMemory<byte> outBuffer, Memory<byte> inBuffer, CancellationToken cancellationToken)
    {
        var req = new CommandRequest(this, cmd.TargetLun);

        var outBufferCount = outBuffer.Length;
        var inBufferMax = inBuffer.Length;

        var toSend = Math.Min(Math.Min(outBufferCount, Session.ImmediateData ? Session.FirstBurstLength : 0), MaxTargetReceiveDataSegmentLength);
        var packet = req.GetBytes(cmd, outBuffer.Span.Slice(0, toSend), true, inBufferMax != 0, outBufferCount != 0, (uint)(outBufferCount != 0 ? outBufferCount : inBufferMax));
        await _stream.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        var numSent = toSend;
        var pktsSent = 0;
        while (numSent < outBufferCount)
        {
            var pdu = ReadPdu();

            var resp = ParseResponse<ReadyToTransferPacket>(pdu);
            var numApproved = (int)resp.DesiredTransferLength;
            var targetTransferTag = resp.TargetTransferTag;

            while (numApproved > 0)
            {
                toSend = Math.Min(Math.Min(outBufferCount - numSent, numApproved), MaxTargetReceiveDataSegmentLength);

                var pkt = new DataOutPacket(this, cmd.TargetLun);
                packet = pkt.GetBytes(outBuffer.Span.Slice(numSent, toSend), toSend == numApproved, pktsSent++, (uint)numSent, targetTransferTag);
                await _stream.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
                await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);

                numApproved -= toSend;
                numSent += toSend;
            }
        }

        var isFinal = false;
        var numRead = 0;
        while (!isFinal)
        {
            var pdu = ReadPdu();

            if (pdu.OpCode == OpCode.ScsiResponse)
            {
                var resp = ParseResponse<Response>(pdu);

                if (resp.StatusPresent && resp.Status == ScsiStatus.CheckCondition)
                {
                    var senseLength = EndianUtilities.ToUInt16BigEndian(pdu.ContentData, 0);
                    var senseData = new byte[senseLength];
                    System.Buffer.BlockCopy(pdu.ContentData, 2, senseData, 0, senseLength);
                    throw new ScsiCommandException(resp.Status, "Target indicated SCSI failure", senseData);
                }

                if (resp.StatusPresent && resp.Status != ScsiStatus.Good)
                {
                    throw new ScsiCommandException(resp.Status, "Target indicated SCSI failure");
                }

                isFinal = resp.Header.FinalPdu;
            }
            else if (pdu.OpCode == OpCode.ScsiDataIn)
            {
                var resp = ParseResponse<DataInPacket>(pdu);

                if (resp.StatusPresent && resp.Status != ScsiStatus.Good)
                {
                    throw new ScsiCommandException(resp.Status, "Target indicated SCSI failure");
                }

                if (resp.ReadData != null)
                {
                    resp.ReadData.CopyTo(inBuffer.Slice((int)resp.BufferOffset));
                    numRead += resp.ReadData.Length;
                }

                isFinal = resp.Header.FinalPdu;
            }
        }

        Session.NextTaskTag();
        Session.NextCommandSequenceNumber();

        return numRead;
    }

    public T Send<T>(ScsiCommand cmd, ReadOnlySpan<byte> buffer, int expected)
        where T : ScsiResponse, new()
    {
        var tempBuffer = ArrayPool<byte>.Shared.Rent(expected);
        try
        {
            var numRead = Send(cmd, buffer, tempBuffer.AsSpan(0, expected));

            var result = new T();
            result.ReadFrom(tempBuffer, 0, numRead);
            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(tempBuffer);
        }
    }

    public IEnumerable<TargetInfo> EnumerateTargets()
    {
        var parameters = new TextBuffer();
        parameters.Add(SendTargetsParameter, "All");

        var paramBuffer = ArrayPool<byte>.Shared.Rent(parameters.Size);
        try
        {
            parameters.WriteTo(paramBuffer, 0);

            var req = new TextRequest(this);
            var packet = req.GetBytes(0, paramBuffer, 0, parameters.Size, true);
            _stream.Write(packet, 0, packet.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(paramBuffer);
        }

        _stream.Flush();

        var pdu = ReadPdu();
        var resp = ParseResponse<TextResponse>(pdu);

        var buffer = new TextBuffer();
        if (resp.TextData != null)
        {
            buffer.ReadFrom(resp.TextData, 0, resp.TextData.Length);
        }

        string currentTarget = null;
        List<TargetAddress> currentAddresses = null;
        foreach (var line in buffer.Lines)
        {
            if (currentTarget == null)
            {
                if (line.Key != TargetNameParameter)
                {
                    throw new InvalidProtocolException($"Unexpected response parameter {line.Key} expected {TargetNameParameter}");
                }

                currentTarget = line.Value;
                currentAddresses = new List<TargetAddress>();
            }
            else if (line.Key == TargetNameParameter)
            {
                yield return new TargetInfo(currentTarget, currentAddresses.ToArray());
                currentTarget = line.Value;
                currentAddresses.Clear();
            }
            else if (line.Key == TargetAddressParameter)
            {
                currentAddresses.Add(TargetAddress.Parse(line.Value));
            }
        }

        if (currentTarget != null)
        {
            yield return new TargetInfo(currentTarget, currentAddresses.ToArray());
        }
    }

    internal void SeenStatusSequenceNumber(uint number)
    {
        if (number != 0 && number != ExpectedStatusSequenceNumber)
        {
            throw new InvalidProtocolException($"Unexpected status sequence number {number}, expected {ExpectedStatusSequenceNumber}");
        }

        ExpectedStatusSequenceNumber = number + 1;
    }

    private void NegotiateSecurity()
    {
        CurrentLoginStage = LoginStages.SecurityNegotiation;

        //
        // Establish the contents of the request
        //
        var parameters = new TextBuffer();

        GetParametersToNegotiate(parameters, KeyUsagePhase.SecurityNegotiation, Session.SessionType);
        Session.GetParametersToNegotiate(parameters, KeyUsagePhase.SecurityNegotiation);

        var authParam = _authenticators[0].Identifier;
        for (var i = 1; i < _authenticators.Length; ++i)
        {
            authParam += $",{_authenticators[i].Identifier}";
        }

        parameters.Add(AuthMethodParameter, authParam);

        //
        // Send the request...
        //
        var paramBuffer = ArrayPool<byte>.Shared.Rent(parameters.Size);
        try
        {
            parameters.WriteTo(paramBuffer, 0);

            var req = new LoginRequest(this);
            var packet = req.GetBytes(paramBuffer, 0, parameters.Size, true);

            _stream.Write(packet, 0, packet.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(paramBuffer);
        }

        _stream.Flush();

        //
        // Read the response...
        //
        var settings = new TextBuffer();

        var pdu = ReadPdu();
        var resp = ParseResponse<LoginResponse>(pdu);

        if (resp.StatusCode != LoginStatusCode.Success)
        {
            throw new LoginException($"iSCSI Target indicated login failure: {resp.StatusCode}");
        }

        if (resp.Continue)
        {
            var ms = new MemoryStream();
            ms.Write(resp.TextData, 0, resp.TextData.Length);

            while (resp.Continue)
            {
                pdu = ReadPdu();
                resp = ParseResponse<LoginResponse>(pdu);
                ms.Write(resp.TextData, 0, resp.TextData.Length);
            }

            settings.ReadFrom(ms.AsSpan());
        }
        else if (resp.TextData != null)
        {
            settings.ReadFrom(resp.TextData, 0, resp.TextData.Length);
        }

        Authenticator authenticator = null;
        for (var i = 0; i < _authenticators.Length; ++i)
        {
            if (settings[AuthMethodParameter] == _authenticators[i].Identifier)
            {
                authenticator = _authenticators[i];
                break;
            }
        }

        settings.Remove(AuthMethodParameter);
        settings.Remove("TargetPortalGroupTag");

        if (authenticator == null)
        {
            throw new LoginException($"iSCSI Target specified an unsupported authentication method: {settings[AuthMethodParameter]}");
        }

        parameters = new TextBuffer();
        ConsumeParameters(settings, parameters);

        while (!resp.Transit)
        {
            //
            // Send the request...
            //
            parameters = new TextBuffer();
            authenticator.GetParameters(parameters);

            paramBuffer = ArrayPool<byte>.Shared.Rent(parameters.Size);
            try
            {
                parameters.WriteTo(paramBuffer, 0);

                var req = new LoginRequest(this);
                var packet = req.GetBytes(paramBuffer, 0, parameters.Size, true);

                _stream.Write(packet, 0, packet.Length);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(paramBuffer);
            }

            _stream.Flush();

            //
            // Read the response...
            //
            settings = new TextBuffer();

            pdu = ReadPdu();
            resp = ParseResponse<LoginResponse>(pdu);

            if (resp.StatusCode != LoginStatusCode.Success)
            {
                throw new LoginException($"iSCSI Target indicated login failure: {resp.StatusCode}");
            }

            if (resp.TextData != null && resp.TextData.Length != 0)
            {
                if (resp.Continue)
                {
                    var ms = new MemoryStream();
                    ms.Write(resp.TextData, 0, resp.TextData.Length);

                    while (resp.Continue)
                    {
                        pdu = ReadPdu();
                        resp = ParseResponse<LoginResponse>(pdu);
                        ms.Write(resp.TextData, 0, resp.TextData.Length);
                    }

                    settings.ReadFrom(ms.AsSpan());
                }
                else
                {
                    settings.ReadFrom(resp.TextData, 0, resp.TextData.Length);
                }

                authenticator.SetParameters(settings);
            }
        }

        if (resp.NextStage != NextLoginStage)
        {
            throw new LoginException($"iSCSI Target wants to transition to a different login stage: {resp.NextStage} (expected: {NextLoginStage})");
        }

        CurrentLoginStage = resp.NextStage;
    }

    private void NegotiateFeatures()
    {
        //
        // Send the request...
        //
        var parameters = new TextBuffer();
        GetParametersToNegotiate(parameters, KeyUsagePhase.OperationalNegotiation, Session.SessionType);
        Session.GetParametersToNegotiate(parameters, KeyUsagePhase.OperationalNegotiation);

        var paramBuffer = ArrayPool<byte>.Shared.Rent(parameters.Size);
        try
        {
            parameters.WriteTo(paramBuffer, 0);

            var req = new LoginRequest(this);
            var packet = req.GetBytes(paramBuffer, 0, parameters.Size, true);

            _stream.Write(packet, 0, packet.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(paramBuffer);
        }

        _stream.Flush();

        //
        // Read the response...
        //
        var settings = new TextBuffer();

        var pdu = ReadPdu();
        var resp = ParseResponse<LoginResponse>(pdu);

        if (resp.StatusCode != LoginStatusCode.Success)
        {
            throw new LoginException($"iSCSI Target indicated login failure: {resp.StatusCode}");
        }

        if (resp.Continue)
        {
            var ms = new MemoryStream();
            ms.Write(resp.TextData, 0, resp.TextData.Length);

            while (resp.Continue)
            {
                pdu = ReadPdu();
                resp = ParseResponse<LoginResponse>(pdu);
                ms.Write(resp.TextData, 0, resp.TextData.Length);
            }

            settings.ReadFrom(ms.AsSpan());
        }
        else if (resp.TextData != null)
        {
            settings.ReadFrom(resp.TextData, 0, resp.TextData.Length);
        }

        parameters = new TextBuffer();
        ConsumeParameters(settings, parameters);

        while (!resp.Transit || parameters.Count != 0)
        {
            paramBuffer = ArrayPool<byte>.Shared.Rent(parameters.Size);
            try
            {
                parameters.WriteTo(paramBuffer, 0);

                var req = new LoginRequest(this);
                var packet = req.GetBytes(paramBuffer, 0, parameters.Size, true);

                _stream.Write(packet, 0, packet.Length);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(paramBuffer);
            }

            _stream.Flush();

            //
            // Read the response...
            //
            settings = new TextBuffer();

            pdu = ReadPdu();
            resp = ParseResponse<LoginResponse>(pdu);

            if (resp.StatusCode != LoginStatusCode.Success)
            {
                throw new LoginException($"iSCSI Target indicated login failure: {resp.StatusCode}");
            }

            parameters = new TextBuffer();

            if (resp.TextData != null)
            {
                if (resp.Continue)
                {
                    var ms = new MemoryStream();
                    ms.Write(resp.TextData, 0, resp.TextData.Length);

                    while (resp.Continue)
                    {
                        pdu = ReadPdu();
                        resp = ParseResponse<LoginResponse>(pdu);
                        ms.Write(resp.TextData, 0, resp.TextData.Length);
                    }

                    settings.ReadFrom(ms.AsSpan());
                }
                else
                {
                    settings.ReadFrom(resp.TextData, 0, resp.TextData.Length);
                }

                ConsumeParameters(settings, parameters);
            }
        }

        if (resp.NextStage != NextLoginStage)
        {
            throw new LoginException($"iSCSI Target wants to transition to a different login stage: {resp.NextStage} (expected: {NextLoginStage})");
        }

        CurrentLoginStage = resp.NextStage;
    }

    private ProtocolDataUnit ReadPdu()
    {
        var pdu = ProtocolDataUnit.ReadFrom(_stream, HeaderDigest != Digest.None, DataDigest != Digest.None);

        if (pdu.OpCode == OpCode.Reject)
        {
            var pkt = new RejectPacket();
            pkt.Parse(pdu);

            throw new IscsiException($"Target sent reject packet, reason {pkt.Reason}");
        }

        return pdu;
    }

    private void GetParametersToNegotiate(TextBuffer parameters, KeyUsagePhase phase, SessionType sessionType)
    {
        var properties = GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var propInfo in properties)
        {
            var attr = propInfo.GetCustomAttribute<ProtocolKeyAttribute>();
            if (attr != null)
            {
                var value = propInfo.GetGetMethod(true).Invoke(this, null);

                if (attr.ShouldTransmit(value, propInfo.PropertyType, phase, sessionType == SessionType.Discovery))
                {
                    parameters.Add(attr.Name, ProtocolKeyAttribute.GetValueAsString(value, propInfo.PropertyType));
                    _negotiatedParameters.Add(attr.Name, string.Empty);
                }
            }
        }
    }

    private void ConsumeParameters(TextBuffer inParameters, TextBuffer outParameters)
    {
        var properties = GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var propInfo in properties)
        {
            var attr = propInfo.GetCustomAttribute<ProtocolKeyAttribute>();
            if (attr != null && (attr.Sender & KeySender.Target) != 0)
            {
                if (inParameters[attr.Name] != null)
                {
                    var value = ProtocolKeyAttribute.GetValueAsObject(inParameters[attr.Name], propInfo.PropertyType);

                    propInfo.GetSetMethod(true).Invoke(this, new[] { value });
                    inParameters.Remove(attr.Name);

                    if (attr.Type == KeyType.Negotiated && !_negotiatedParameters.ContainsKey(attr.Name))
                    {
                        value = propInfo.GetGetMethod(true).Invoke(this, null);
                        outParameters.Add(attr.Name, ProtocolKeyAttribute.GetValueAsString(value, propInfo.PropertyType));
                        _negotiatedParameters.Add(attr.Name, string.Empty);
                    }
                }
            }
        }

        Session.ConsumeParameters(inParameters, outParameters);

        foreach (var param in inParameters.Lines)
        {
            outParameters.Add(param.Key, "NotUnderstood");
        }
    }

    private T ParseResponse<T>(ProtocolDataUnit pdu)
        where T : BaseResponse, new()
    {
        BaseResponse resp = pdu.OpCode switch
        {
            OpCode.LoginResponse => new LoginResponse(),
            OpCode.LogoutResponse => new LogoutResponse(),
            OpCode.ReadyToTransfer => new ReadyToTransferPacket(),
            OpCode.Reject => new RejectPacket(),
            OpCode.ScsiDataIn => new DataInPacket(),
            OpCode.ScsiResponse => new Response(),
            OpCode.TextResponse => new TextResponse(),
            _ => throw new InvalidProtocolException($"Unrecognized response opcode: {pdu.OpCode}"),
        };
        resp.Parse(pdu);
        if (resp.StatusPresent)
        {
            SeenStatusSequenceNumber(resp.StatusSequenceNumber);
        }

        return resp is T result
            ? result
            : throw new InvalidProtocolException($"Unexpected response, expected {typeof(T)}, got {resp.GetType()}");
    }

    #region Parameters

    internal const string InitiatorNameParameter = "InitiatorName";
    internal const string SessionTypeParameter = "SessionType";
    internal const string AuthMethodParameter = "AuthMethod";

    internal const string HeaderDigestParameter = "HeaderDigest";
    internal const string DataDigestParameter = "DataDigest";
    internal const string MaxRecvDataSegmentLengthParameter = "MaxRecvDataSegmentLength";
    internal const string DefaultTime2WaitParameter = "DefaultTime2Wait";
    internal const string DefaultTime2RetainParameter = "DefaultTime2Retain";

    internal const string SendTargetsParameter = "SendTargets";
    internal const string TargetNameParameter = "TargetName";
    internal const string TargetAddressParameter = "TargetAddress";

    internal const string NoneValue = "None";
    internal const string ChapValue = "CHAP";

    #endregion

    #region Protocol Features

    [ProtocolKey("HeaderDigest", "None", KeyUsagePhase.OperationalNegotiation, KeySender.Both, KeyType.Negotiated, UsedForDiscovery = true)]
    public Digest HeaderDigest { get; set; }

    [ProtocolKey("DataDigest", "None", KeyUsagePhase.OperationalNegotiation, KeySender.Both, KeyType.Negotiated, UsedForDiscovery = true)]
    public Digest DataDigest { get; set; }

    [ProtocolKey("MaxRecvDataSegmentLength", "8192", KeyUsagePhase.OperationalNegotiation, KeySender.Initiator, KeyType.Declarative)]
    internal int MaxInitiatorTransmitDataSegmentLength { get; set; }

    [ProtocolKey("MaxRecvDataSegmentLength", "8192", KeyUsagePhase.OperationalNegotiation, KeySender.Target, KeyType.Declarative)]
    internal int MaxTargetReceiveDataSegmentLength { get; set; }

    #endregion
}