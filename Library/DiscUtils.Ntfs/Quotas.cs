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
using DiscUtils.Core.WindowsSecurity;
using DiscUtils.Streams;

namespace DiscUtils.Ntfs;

internal sealed class Quotas
{
    private readonly IndexView<OwnerKey, OwnerRecord> _ownerIndex;
    private readonly IndexView<OwnerRecord, QuotaRecord> _quotaIndex;

    public Quotas(File file)
    {
        _ownerIndex = new IndexView<OwnerKey, OwnerRecord>(file.GetIndex("$O"));
        _quotaIndex = new IndexView<OwnerRecord, QuotaRecord>(file.GetIndex("$Q"));
    }

    public static Quotas Initialize(File file)
    {
        var ownerIndex = file.CreateIndex("$O", 0, AttributeCollationRule.Sid);
        var quotaIndox = file.CreateIndex("$Q", 0, AttributeCollationRule.UnsignedLong);

        var ownerIndexView = new IndexView<OwnerKey, OwnerRecord>(ownerIndex);
        var quotaIndexView = new IndexView<OwnerRecord, QuotaRecord>(quotaIndox);

        var adminSid = new OwnerKey(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        var adminOwnerId = new OwnerRecord(256);

        ownerIndexView[adminSid] = adminOwnerId;

        quotaIndexView[new OwnerRecord(1)] = new QuotaRecord(null);
        quotaIndexView[adminOwnerId] = new QuotaRecord(adminSid.Sid);

        return new Quotas(file);
    }

    public void Dump(TextWriter writer, string indent)
    {
        writer.WriteLine($"{indent}QUOTAS");

        writer.WriteLine($"{indent}  OWNER INDEX");
        foreach (var entry in _ownerIndex.Entries)
        {
            writer.WriteLine($"{indent}    OWNER INDEX ENTRY");
            writer.WriteLine($"{indent}            SID: {entry.Key.Sid}");
            writer.WriteLine($"{indent}       Owner Id: {entry.Value.OwnerId}");
        }

        writer.WriteLine($"{indent}  QUOTA INDEX");
        foreach (var entry in _quotaIndex.Entries)
        {
            writer.WriteLine($"{indent}    QUOTA INDEX ENTRY");
            writer.WriteLine($"{indent}           Owner Id: {entry.Key.OwnerId}");
            writer.WriteLine($"{indent}           User SID: {entry.Value.Sid}");
            writer.WriteLine($"{indent}            Changed: {entry.Value.ChangeTime}");
            writer.WriteLine($"{indent}           Exceeded: {entry.Value.ExceededTime}");
            writer.WriteLine($"{indent}         Bytes Used: {entry.Value.BytesUsed}");
            writer.WriteLine($"{indent}              Flags: {entry.Value.Flags}");
            writer.WriteLine($"{indent}         Hard Limit: {entry.Value.HardLimit}");
            writer.WriteLine($"{indent}      Warning Limit: {entry.Value.WarningLimit}");
            writer.WriteLine($"{indent}            Version: {entry.Value.Version}");
        }
    }

    internal struct OwnerKey : IByteArraySerializable
    {
        public SecurityIdentifier Sid;

        public OwnerKey(SecurityIdentifier sid)
        {
            Sid = sid;
        }

        public int Size => Sid.BinaryLength;

        public int ReadFrom(ReadOnlySpan<byte> buffer)
        {
            Sid = new SecurityIdentifier(buffer);
            return Sid.BinaryLength;
        }

        public void WriteTo(Span<byte> buffer)
        {
            Sid.GetBinaryForm(buffer);
        }

        public override string ToString() => $"[Sid:{Sid}]";
    }

    internal struct OwnerRecord : IByteArraySerializable
    {
        public int OwnerId;

        public OwnerRecord(int ownerId)
        {
            OwnerId = ownerId;
        }

        public int Size => 4;

        public int ReadFrom(ReadOnlySpan<byte> buffer)
        {
            OwnerId = EndianUtilities.ToInt32LittleEndian(buffer);
            return 4;
        }

        public void WriteTo(Span<byte> buffer)
        {
            EndianUtilities.WriteBytesLittleEndian(OwnerId, buffer);
        }

        public override string ToString()
        {
            return $"[OwnerId:{OwnerId}]";
        }
    }

    internal struct QuotaRecord : IByteArraySerializable
    {
        public long BytesUsed;
        public DateTime ChangeTime;
        public long ExceededTime;
        public int Flags;
        public long HardLimit;
        public SecurityIdentifier Sid;
        public int Version;
        public long WarningLimit;

        public QuotaRecord(SecurityIdentifier sid)
        {
            BytesUsed = 0;
            Version = 2;
            Flags = 1;
            ChangeTime = DateTime.UtcNow;
            ExceededTime = 0;
            WarningLimit = -1;
            HardLimit = -1;
            Sid = sid;
        }

        public int Size => 0x30 + (Sid == null ? 0 : Sid.BinaryLength);

        public int ReadFrom(ReadOnlySpan<byte> buffer)
        {
            Version = EndianUtilities.ToInt32LittleEndian(buffer);
            Flags = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x04));
            BytesUsed = EndianUtilities.ToInt64LittleEndian(buffer.Slice(0x08));
            ChangeTime = DateTime.FromFileTimeUtc(EndianUtilities.ToInt64LittleEndian(buffer.Slice(0x10)));
            WarningLimit = EndianUtilities.ToInt64LittleEndian(buffer.Slice(0x18));
            HardLimit = EndianUtilities.ToInt64LittleEndian(buffer.Slice(0x20));
            ExceededTime = EndianUtilities.ToInt64LittleEndian(buffer.Slice(0x28));
            if (buffer.Length > 0x30)
            {
                Sid = new SecurityIdentifier(buffer.Slice(0x30));
                return 0x30 + Sid.BinaryLength;
            }

            return 0x30;
        }

        public void WriteTo(Span<byte> buffer)
        {
            EndianUtilities.WriteBytesLittleEndian(Version, buffer);
            EndianUtilities.WriteBytesLittleEndian(Flags, buffer.Slice(0x04));
            EndianUtilities.WriteBytesLittleEndian(BytesUsed, buffer.Slice(0x08));
            EndianUtilities.WriteBytesLittleEndian(ChangeTime.ToFileTimeUtc(), buffer.Slice(0x10));
            EndianUtilities.WriteBytesLittleEndian(WarningLimit, buffer.Slice(0x18));
            EndianUtilities.WriteBytesLittleEndian(HardLimit, buffer.Slice(0x20));
            EndianUtilities.WriteBytesLittleEndian(ExceededTime, buffer.Slice(0x28));
            Sid?.GetBinaryForm(buffer.Slice(0x30));
        }

        public override string ToString()
        {
            return $"[V:{Version},F:{Flags},BU:{BytesUsed},CT:{ChangeTime},WL:{WarningLimit},HL:{HardLimit},ET:{ExceededTime},SID:{Sid}]";
        }
    }
}