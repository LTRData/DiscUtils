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

using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;
using LTRData.Extensions.Buffers;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace DiscUtils.Iscsi;

internal sealed class Connection : IDisposable
{
    private readonly Authenticator[] _authenticators;

    /// <summary>
    /// The set of all 'parameters' we've negotiated.
    /// </summary>
    private readonly Dictionary<string, string> _negotiatedParameters;

    private readonly NetworkStream _stream;

    /// <summary>
    /// Semaphore to synchronize access to the network stream. NOP-Out responses from the
    /// keepalive timer and normal Send/ReadPdu traffic must not interleave.
    /// </summary>
    private readonly SemaphoreSlim _streamSemaphore = new(1, 1);

    /// <summary>
    /// Timer that periodically sends NOP-Out pings to keep the iSCSI session alive.
    /// </summary>
    private Timer _keepAliveTimer;

    public Connection(Session session, TargetAddress address, Authenticator[] authenticators)
    {
        Session = session;
        _authenticators = authenticators;

        var client = new TcpClient(address.NetworkAddress, address.NetworkPort)
        {
            NoDelay = true
        };

        var socket = client.Client;

        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

#if NET5_0_OR_GREATER
        // Linux-only options (no-op on Windows before .NET 7)
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 15); // seconds
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 5);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
#endif

        _stream = client.GetStream();

        Id = session.NextConnectionId();

        // Default negotiated values
        HeaderDigest = Digest.None;
        DataDigest = Digest.None;
        MaxInitiatorTransmitDataSegmentLength = 8 << 20;
        MaxTargetReceiveDataSegmentLength = 8 << 20;

        _negotiatedParameters = [];
        NegotiateSecurity();
        NegotiateFeatures();

        // Start keepalive timer after login completes.
        // Send a NOP-Out every 10 seconds to prevent the target from
        // timing out the session during idle periods.
        _keepAliveTimer = new Timer(callback: KeepAliveCallback,
                                    state: null,
                                    dueTime: TimeSpan.FromSeconds(10),
                                    period: TimeSpan.FromSeconds(10));
    }

    internal LoginStages CurrentLoginStage { get; private set; } = LoginStages.SecurityNegotiation;

    internal uint ExpectedStatusSequenceNumber { get; private set; } = 1;
    
    internal ushort Id { get; }

    internal LoginStages NextLoginStage => CurrentLoginStage switch
    {
        LoginStages.SecurityNegotiation => LoginStages.LoginOperationalNegotiation,
        LoginStages.LoginOperationalNegotiation => LoginStages.FullFeaturePhase,
        _ => LoginStages.FullFeaturePhase,
    };

    internal Session Session { get; }

    public void Dispose()
    {
        Close(LogoutReason.CloseConnection);
    }

    public void Close(LogoutReason reason)
    {
        try
        {
            _streamSemaphore.Wait();
            try
            {
                // Stop the keepalive timer while holding the semaphore
                // to ensure no callback is in-flight.
                _keepAliveTimer?.Dispose();
                _keepAliveTimer = null;

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
            }
            finally
            {
                _streamSemaphore.Release();
            }
        }
        catch (EndOfStreamException)
        {
            // Target may have already closed the connection - this is non-fatal during logout
        }
        catch (IOException ex) when (ex.InnerException is SocketException)
        {
            // Target forcibly closed connection - this can happen during logout if target closes first
        }
        finally
        {
            _stream.Dispose();
        }
    }

    /// <summary>
    /// Timer callback: sends a NOP-Out to keep the iSCSI session alive.
    /// Uses ITT=0xFFFFFFFF and TTT=0xFFFFFFFF so the target does not send a NOP-In response
    /// (RFC 3720 §10.18), avoiding read-side contention with the main Send path.
    /// </summary>
    private async void KeepAliveCallback(object _)
    {
        if (!_streamSemaphore.Wait(0))
        {
            // A Send or Close is in progress; skip this ping and retry on the next timer tick.
            return;
        }

        try
        {
#if DEBUG
            Trace.WriteLine("Sending keep-alive...");
#endif

            await SendNopOutAsync().ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Connection is dead — the next Send will surface the error.
        }
        catch (ObjectDisposedException)
        {
            // Stream was disposed — connection is shutting down.
        }
        finally
        {
            _streamSemaphore.Release();
        }
    }

    private byte[] nopOut;

    /// <summary>
    /// Sends an initiator-initiated NOP-Out with ITT=0xFFFFFFFF and TTT=0xFFFFFFFF.
    /// This is a "ping" that keeps the iSCSI session alive without requiring a response.
    /// </summary>
    private async ValueTask SendNopOutAsync()
    {
        if (nopOut is null)
        {
            nopOut = new byte[48];

            // Byte 0: Immediate (0x40) | OpCode NopOut (0x00)
            nopOut[0] = 0x40;
            // Byte 1: Final bit
            nopOut[1] = 0x80;
            // Bytes 16-19: ITT = 0xFFFFFFFF (no response expected)
            EndianUtilities.WriteBytesBigEndian(0xFFFFFFFF, nopOut, 16);
            // Bytes 20-23: TTT = 0xFFFFFFFF
            EndianUtilities.WriteBytesBigEndian(0xFFFFFFFF, nopOut, 20);
        }

        // Bytes 24-27: CmdSN (not incremented for immediate PDUs)
        EndianUtilities.WriteBytesBigEndian(Session.CommandSequenceNumber, nopOut, 24);
        // Bytes 28-31: ExpStatSN
        EndianUtilities.WriteBytesBigEndian(ExpectedStatusSequenceNumber, nopOut, 28);

        await _stream.WriteAsync(nopOut).ConfigureAwait(false);

        await _stream.FlushAsync().ConfigureAwait(false);
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
        _streamSemaphore.Wait();

        try
        {
            for (var i = 0; ; i++)
            {
                try
                {
                    // RFC 3720 Bug #8a: Allocate new Task Tag for this command
                    // Originally added because we thought each command must have a unique ITT
                    // TESTING RESULT: NOT NECESSARY - Test passes without this call
                    // The Session already maintains ITT correctly without explicit increment here
                    // Leaving commented for reference in case unique ITT per command is needed in future
                    //Session.NextTaskTag();

                    // RFC 3720: DataSN starts at 0 for each new command
                    var expectedDataSN = 0u;

                    var req = new CommandRequest(this, cmd.TargetLun);

                    var outBufferCount = outBuffer.Length;
                    var inBufferMax = inBuffer.Length;

                    var toSend = Math.Min(outBufferCount, Session.ImmediateData ? Session.FirstBurstLength : 0);

                    // F bit (isFinalData) = true means "no more unsolicited Data-Out PDUs will follow".
                    // Even if we're sending < outBufferCount, we set F=1 because any remaining data
                    // will be sent via solicited Data-Out (after R2T), not more unsolicited Data-Out.

                    // Simpler logic: Use buffer sizes directly
                    var expectedTransferLength = outBufferCount != 0 ? (uint)outBufferCount : (uint)inBufferMax;

                    var packet = req.GetBytes(cmd,
                                              immediateData: outBuffer.Slice(0, toSend),
                                              isFinalData: true,
                                              willRead: inBufferMax != 0,
                                              willWrite: outBufferCount != 0,
                                              expected: expectedTransferLength);

                    _stream.Write(packet, 0, packet.Length);
                    _stream.Flush();

                    var numSent = toSend;
                    while (numSent < outBufferCount)
                    {
                        var pdu = ReadPdu();

                        var resp = ParseResponse<ReadyToTransferPacket>(pdu);
                        var numApproved = (int)resp.DesiredTransferLength;
                        var targetTransferTag = resp.TargetTransferTag;

                        // DataSN resets to 0 for each R2T (not continuous across multiple R2Ts)
                        var pktsSent = 0;
                        while (numApproved > 0)
                        {
                            toSend = Math.Min(Math.Min(outBufferCount - numSent, numApproved), MaxTargetReceiveDataSegmentLength ?? MaxInitiatorTransmitDataSegmentLength);

                            var pkt = new DataOutPacket(this, cmd.TargetLun);
                            var currentDataSN = pktsSent++;

                            packet = pkt.GetBytes(data: outBuffer.Slice(numSent, toSend),
                                                  isFinalData: toSend == numApproved,
                                                  dataSeqNumber: currentDataSN,
                                                  bufferOffset: (uint)numSent,
                                                  targetTransferTag: targetTransferTag);

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
                                var senseData = pdu.ContentData.AsSpan(2, senseLength).ToArray();

                                if (i == 0 && ScsiSenseParser.TryParse(senseData, out var sense) && sense.IndicatesRetryRequired)
                                {
                                    goto retry;
                                }

                                throw new ScsiCommandException(resp.Status, senseData);
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

                            // RFC 3720 Section 10.7.4: Validate DataSN sequence
                            if (resp.DataSequenceNumber != expectedDataSN)
                            {
                                throw new InvalidProtocolException($"DataSN mismatch: received {resp.DataSequenceNumber}, expected {expectedDataSN}");
                            }

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

                            expectedDataSN++;
                        }
                    }

                    return Math.Max(numRead, numSent);
                }
                finally
                {
                    Session.NextTaskTag();
                    Session.NextCommandSequenceNumber();
                }

                retry:
                {
                }
            }
        }
        finally
        {
            _streamSemaphore.Release();
        }
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
        await _streamSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            for (var i = 0; ; i++)
            {
                try
                {
                    // RFC 3720: DataSN starts at 0 for each new command
                    var expectedDataSN = 0u;

                    var req = new CommandRequest(this, cmd.TargetLun);

                    var outBufferCount = outBuffer.Length;
                    var inBufferMax = inBuffer.Length;

                    var toSend = Math.Min(outBufferCount, Session.ImmediateData ? Session.FirstBurstLength : 0);

                    // F bit (isFinalData) = true means "no more unsolicited Data-Out PDUs will follow".
                    // Even if we're sending < outBufferCount, we set F=1 because any remaining data
                    // will be sent via solicited Data-Out (after R2T), not more unsolicited Data-Out.

                    // Simpler logic: Use buffer sizes directly
                    var expectedTransferLength = outBufferCount != 0 ? (uint)outBufferCount : (uint)inBufferMax;

                    var packet = req.GetBytes(cmd, outBuffer.Span.Slice(0, toSend), true, inBufferMax != 0, outBufferCount != 0, expectedTransferLength);
                    await _stream.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
                    await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    var numSent = toSend;
                    var pktsSent = 0;
                    while (numSent < outBufferCount)
                    {
                        var pdu = await ReadPduAsync(cancellationToken).ConfigureAwait(false);

                        var resp = ParseResponse<ReadyToTransferPacket>(pdu);
                        var numApproved = (int)resp.DesiredTransferLength;
                        var targetTransferTag = resp.TargetTransferTag;

                        while (numApproved > 0)
                        {
                            toSend = Math.Min(Math.Min(outBufferCount - numSent, numApproved), MaxTargetReceiveDataSegmentLength ?? MaxInitiatorTransmitDataSegmentLength);

                            var pkt = new DataOutPacket(this, cmd.TargetLun);
                            var currentDataSN = pktsSent++;
                            packet = pkt.GetBytes(outBuffer.Span.Slice(numSent, toSend), toSend == numApproved, currentDataSN, (uint)numSent, targetTransferTag);
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
                        var pdu = await ReadPduAsync(cancellationToken).ConfigureAwait(false);

                        if (pdu.OpCode == OpCode.ScsiResponse)
                        {
                            var resp = ParseResponse<Response>(pdu);

                            if (resp.StatusPresent && resp.Status == ScsiStatus.CheckCondition)
                            {
                                var senseLength = EndianUtilities.ToUInt16BigEndian(pdu.ContentData, 0);
                                var senseData = pdu.ContentData.AsSpan(2, senseLength).ToArray();

                                if (i == 0 && ScsiSenseParser.TryParse(senseData, out var sense) && sense.IndicatesRetryRequired)
                                {
                                    goto retry;
                                }

                                throw new ScsiCommandException(resp.Status, senseData);
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

                            // RFC 3720 Section 10.7.4: Validate DataSN sequence
                            if (resp.DataSequenceNumber != expectedDataSN)
                            {
                                throw new InvalidProtocolException($"DataSN mismatch: received {resp.DataSequenceNumber}, expected {expectedDataSN}");
                            }
                            expectedDataSN++;

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

                    return Math.Max(numRead, numSent);
                }
                finally
                {
                    Session.NextTaskTag();
                    Session.NextCommandSequenceNumber();
                }

                retry:
                {
                }
            }
        }
        finally
        {
            _streamSemaphore.Release();
        }
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
                currentAddresses = [];
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

        // RFC 3720: StatSN stays constant during Login Phase, only increments in Full Feature Phase
        if (CurrentLoginStage == LoginStages.FullFeaturePhase)
        {
            ExpectedStatusSequenceNumber = number + 1;
        }
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
            if (resp.TextData != null && resp.TextData.Length > 0)
            {
                var tempSettings = new TextBuffer();
                tempSettings.ReadFrom(resp.TextData, 0, resp.TextData.Length);

                throw new LoginException($"iSCSI Target indicated login failure: {resp.StatusCode}: {string.Join(", ", tempSettings.Lines.Select(line => $"{line.Key} = {line.Value}"))}");
            }

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

        // Initialize ExpectedStatusSequenceNumber for Full Feature Phase
        // Some targets (like TrueNAS) use TargetTransferTag+1 as the starting StatSN
        if (CurrentLoginStage == LoginStages.FullFeaturePhase)
        {
            var initialStatSN = resp.TargetTransferTag + 1;
            ExpectedStatusSequenceNumber = initialStatSN;
        }
    }

    private ProtocolDataUnit ReadPdu()
    {
        const int MaxNopInRetries = 16;
        for (var nopCount = 0; ; nopCount++)
        {
            var pdu = ProtocolDataUnit.ReadFrom(_stream, HeaderDigest != Digest.None, DataDigest != Digest.None);

            if (pdu.OpCode == OpCode.Reject)
            {
                var pkt = new RejectPacket();
                pkt.Parse(pdu);

                throw new IscsiException($"Target sent reject packet, reason {pkt.Reason}");
            }

            // RFC 3720 §10.19: Handle target-initiated NOP-In (ping).
            // Respond with NOP-Out echoing the Target Transfer Tag, then
            // continue reading the next PDU.
            if (pdu.OpCode == OpCode.NopIn)
            {
                if (nopCount >= MaxNopInRetries)
                {
                    throw new InvalidProtocolException($"Received {nopCount} consecutive NOP-In PDUs without a command response");
                }

                HandleNopIn(pdu);
                continue;
            }

            return pdu;
        }
    }

    private async ValueTask<ProtocolDataUnit> ReadPduAsync(CancellationToken cancellationToken)
    {
        const int MaxNopInRetries = 16;
        for (var nopCount = 0; ; nopCount++)
        {
            var pdu = await ProtocolDataUnit.ReadFromAsync(_stream, HeaderDigest != Digest.None, DataDigest != Digest.None, cancellationToken).ConfigureAwait(false);

            if (pdu.OpCode == OpCode.Reject)
            {
                var pkt = new RejectPacket();
                pkt.Parse(pdu);

                throw new IscsiException($"Target sent reject packet, reason {pkt.Reason}");
            }

            if (pdu.OpCode == OpCode.NopIn)
            {
                if (nopCount >= MaxNopInRetries)
                {
                    throw new InvalidProtocolException($"Received {nopCount} consecutive NOP-In PDUs without a command response");
                }

                HandleNopIn(pdu);
                continue;
            }

            return pdu;
        }
    }

    /// <summary>
    /// Responds to a target-initiated NOP-In with a NOP-Out (RFC 3720 §10.18/10.19).
    /// Target-initiated NOP-In has TTT != 0xFFFFFFFF and requires a NOP-Out reply.
    /// The StatSN in a target-initiated NOP-In is informational and does NOT consume
    /// a sequence number, so we must NOT call SeenStatusSequenceNumber here.
    /// </summary>
    private void HandleNopIn(ProtocolDataUnit pdu)
    {
        var headerData = pdu.HeaderData;

        // Target Transfer Tag at offset 20
        var targetTransferTag = EndianUtilities.ToUInt32BigEndian(headerData.AsSpan(20));

        // If TTT is 0xFFFFFFFF, this is a response to our own NOP-Out (unsolicited);
        // no reply needed.
        if (targetTransferTag == 0xFFFFFFFF)
        {
            return;
        }

        // LUN at offset 8
        var lun = EndianUtilities.ToUInt64BigEndian(headerData.AsSpan(8));

        // NOTE: We intentionally do NOT call SeenStatusSequenceNumber() here.
        // RFC 3720 §10.19: A target-initiated NOP-In carries a StatSN, but this
        // StatSN is NOT consumed — it's the same value the target will use for the
        // next real command response. Advancing ExpectedStatusSequenceNumber here
        // would cause the next ParseResponse to fail with a sequence mismatch.

        // Build NOP-Out response (RFC 3720 §10.18)
        var nopOut = new byte[48];
        // Byte 0: Immediate bit (0x40) | OpCode NopOut (0x00)
        nopOut[0] = 0x40;
        // Byte 1: Final bit (0x80)
        nopOut[1] = 0x80;
        // Bytes 8-15: LUN (copy from NOP-In)
        EndianUtilities.WriteBytesBigEndian(lun, nopOut, 8);
        // Bytes 16-19: Initiator Task Tag = 0xFFFFFFFF (response to target ping)
        EndianUtilities.WriteBytesBigEndian(0xFFFFFFFF, nopOut, 16);
        // Bytes 20-23: Target Transfer Tag (echo from NOP-In)
        EndianUtilities.WriteBytesBigEndian(targetTransferTag, nopOut, 20);
        // Bytes 24-27: CmdSN (not consumed for immediate PDUs with ITT=0xFFFFFFFF)
        EndianUtilities.WriteBytesBigEndian(Session.CommandSequenceNumber, nopOut, 24);
        // Bytes 28-31: ExpStatSN
        EndianUtilities.WriteBytesBigEndian(ExpectedStatusSequenceNumber, nopOut, 28);

        _stream.Write(nopOut, 0, nopOut.Length);
        _stream.Flush();
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

                    propInfo.GetSetMethod(true).Invoke(this, [value]);
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

    [ProtocolKey("MaxRecvDataSegmentLength", "", KeyUsagePhase.OperationalNegotiation, KeySender.Both, KeyType.Declarative)]
    public int MaxInitiatorTransmitDataSegmentLength { get; set; }

    [ProtocolKey("TargetRecvDataSegmentLength", "", KeyUsagePhase.OperationalNegotiation, KeySender.Both, KeyType.Declarative)]
    public int? MaxTargetReceiveDataSegmentLength { get; set; }

    #endregion
}