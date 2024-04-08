using DiscUtils.Streams;
using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace DiscUtils.Core.WindowsSecurity;

[ComVisible(false)]
public sealed class SecurityIdentifier : IdentityReference, IComparable<SecurityIdentifier>
{
    private byte[] buffer;

    public static readonly int MaxBinaryLength = 68;
    public static readonly int MinBinaryLength = 8;

    public SecurityIdentifier(string sddlForm)
    {
        if (sddlForm == null)
        {
            throw new ArgumentNullException(nameof(sddlForm));
        }

        buffer = ParseSddlForm(sddlForm);
    }

    public SecurityIdentifier(ReadOnlySpan<char> sddlForm)
    {
        buffer = ParseSddlForm(sddlForm);
    }

    public SecurityIdentifier(byte[] binaryForm, int offset)
        : this(binaryForm.AsSpan(offset))
    {
    }

    private SecurityIdentifier() { }

    public static bool TryParse(byte[] binaryForm, int offset, out SecurityIdentifier securityIdentifier) =>
        TryParse(binaryForm.AsSpan(offset), out securityIdentifier);

    public bool TryCreateFromBinaryForm(byte[] binaryForm, int offset) =>
        TryCreateFromBinaryForm(binaryForm.AsSpan(offset));

    public SecurityIdentifier(ReadOnlySpan<byte> binaryForm)
    {
        if (binaryForm.IsEmpty)
        {
            throw new ArgumentNullException(nameof(binaryForm));
        }

        if (2 > binaryForm.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(binaryForm), binaryForm.Length, "Invalid binary length");
        }

        if (!TryCreateFromBinaryForm(binaryForm))
        {
            throw new ArgumentException("Invalid security identifier");
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> binaryForm, out SecurityIdentifier securityIdentifier)
    {
        securityIdentifier = null;

        if (binaryForm == null || (2 > binaryForm.Length))
        {
            return false;
        }

        var sid = new SecurityIdentifier();
        if (!sid.TryCreateFromBinaryForm(binaryForm))
        {
            return false;
        }

        securityIdentifier = sid;
        return true;
    }

    bool TryCreateFromBinaryForm(ReadOnlySpan<byte> binaryForm)
    {
        int revision = binaryForm[0];
        int numSubAuthorities = binaryForm[1];
        if (revision != 1 || numSubAuthorities > 15 || binaryForm.Length < (8 + (numSubAuthorities * 4)))
        {
            return false;
        }

        buffer = binaryForm.Slice(0, 8 + numSubAuthorities * 4).ToArray();
        return true;
    }

    public SecurityIdentifier CreateSubSid(uint rid)
    {
        Span<byte> newBinary = stackalloc byte[buffer.Length + 4];
        buffer.CopyTo(newBinary);
        var numSubAuthorities = newBinary[1]++;
        if (numSubAuthorities > 15)
        {
            throw new InvalidOperationException("Too many subauthorities.");
        }

        EndianUtilities.WriteBytesLittleEndian(rid, newBinary.Slice(newBinary.Length - 4));
        return new(newBinary);
    }

    public SecurityIdentifier(WellKnownSidType sidType,
                              SecurityIdentifier domainSid)
    {
        var acct = WellKnownAccount.LookupByType(sidType);
        if (acct == null)
        {
            throw new ArgumentException($"Unable to convert SID type: {sidType}");
        }

        if (acct.IsAbsolute)
        {
            buffer = acct.Sid.buffer;
        }
        else
        {
            if (domainSid == null)
            {
                throw new ArgumentNullException(nameof(domainSid));
            }

            buffer = domainSid.CreateSubSid(uint.Parse(acct.RidStr)).buffer;
        }
    }

    public SecurityIdentifier AccountDomainSid
    {
        get
        {
            if (!IsAccountSid())
            {
                return null;
            }

            // Domain is first four sub-authorities
            Span<byte> temp = stackalloc byte[8 + (4 * 4)];
            buffer.AsSpan(0, temp.Length).CopyTo(temp);
            temp[1] = 4;
            return new SecurityIdentifier(temp);
        }
    }

    public uint AccountUserId
    {
        get
        {
            if (!IsAccountSid())
            {
                return 0;
            }

            var rid = GetSidSubAuthority((byte)(SidSubAuthorityCount - 1));
            return rid;
        }
    }

    public int BinaryLength => buffer.Length;

    public override string Value
    {
        get
        {
            var s = new StringBuilder();

            var authority = SidAuthority;
            s.AppendFormat(CultureInfo.InvariantCulture, "S-1-{0}", authority);

            for (byte i = 0; i < SidSubAuthorityCount; ++i)
            {
                s.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "-{0}", GetSidSubAuthority(i));
            }

            return s.ToString();
        }
    }

    public ulong SidAuthority => (((ulong)buffer[2]) << 40) | (((ulong)buffer[3]) << 32)
                                          | (((ulong)buffer[4]) << 24) | (((ulong)buffer[5]) << 16)
                                          | (((ulong)buffer[6]) << 8) | (((ulong)buffer[7]) << 0);

    public byte SidSubAuthorityCount => buffer[1];

    public uint GetSidSubAuthority(byte index)
    {
        // Note sub authorities little-endian, authority (above) is big-endian!
        var offset = 8 + (index * 4);

        return (((uint)buffer[offset + 0]) << 0)
               | (((uint)buffer[offset + 1]) << 8)
               | (((uint)buffer[offset + 2]) << 16)
               | (((uint)buffer[offset + 3]) << 24);
    }

    // The CompareTo ordering was determined by unit test applied to MS.NET implementation,
    // necessary because the CompareTo has no details in its documentation.
    // (See MonoTests.System.Security.AccessControl.DiscretionaryAclTest.)
    // The comparison was determined to be: authority, then subauthority count, then subauthority.
    public int CompareTo(SecurityIdentifier sid)
    {
        if (sid == null)
        {
            throw new ArgumentNullException(nameof(sid));
        }

        int result;
        if (0 != (result = SidAuthority.CompareTo(sid.SidAuthority)))
        {
            return result;
        }

        if (0 != (result = SidSubAuthorityCount.CompareTo(sid.SidSubAuthorityCount)))
        {
            return result;
        }

        for (byte i = 0; i < SidSubAuthorityCount; ++i)
        {
            if (0 != (result = GetSidSubAuthority(i).CompareTo(sid.GetSidSubAuthority(i))))
            {
                return result;
            }
        }

        return 0;
    }

    public override bool Equals(object o) => Equals(o as SecurityIdentifier);

    public bool Equals(SecurityIdentifier sid)
    {
        if (sid is null)
        {
            return false;
        }

        return sid == this;
    }

    public void GetBinaryForm(byte[] binaryForm, int offset) => GetBinaryForm(binaryForm.AsSpan(offset));

    public void GetBinaryForm(Span<byte> binaryForm) => buffer.CopyTo(binaryForm);

    public ReadOnlySpan<byte> AsSpan() => buffer.AsSpan();

    public override int GetHashCode() => Value.GetHashCode();

    public bool IsAccountSid() => buffer[0] == 1
                && SidSubAuthorityCount >= 4
                && SidAuthority == 5
                && GetSidSubAuthority(0) == 21;

    public bool IsEqualDomainSid(SecurityIdentifier sid)
    {
        var domSid = AccountDomainSid;
        if (domSid == null)
        {
            return false;
        }

        return domSid.Equals(sid.AccountDomainSid);
    }

    public override bool IsValidTargetType(Type targetType)
    {
        if (targetType == typeof(SecurityIdentifier))
        {
            return true;
        }

        if (targetType == typeof(NTAccount))
        {
            return true;
        }

        return false;
    }

    public bool IsWellKnown(WellKnownSidType type)
    {
        var acct = WellKnownAccount.LookupByType(type);
        if (acct == null)
        {
            return false;
        }

        if (acct.IsAbsolute)
        {
            return this == acct.Sid;
        }

        return IsAccountSid()
            && AccountUserId == acct.Rid;
    }

    public override string ToString() => Value;

    public override IdentityReference Translate(Type targetType)
    {
        if (targetType == typeof(SecurityIdentifier))
        {
            return this;
        }

        if (targetType == typeof(NTAccount))
        {
            var acct = WellKnownAccount.LookupBySid(Value);
            if (acct?.Name == null)
            {
                throw new Exception($"Unable to map SID: {Value}");
            }

            return new NTAccount(acct.Name);
        }

        throw new ArgumentException("Unknown type.", nameof(targetType));
    }

    public static bool operator ==(SecurityIdentifier left, SecurityIdentifier right)
    {
        if (left is null)
        {
            return right is null;
        }

        if (right is null)
        {
            return false;
        }

        return left.buffer.Length == right.buffer.Length
            && left.buffer.SequenceEqual(right.buffer);
    }

    public static bool operator !=(SecurityIdentifier left, SecurityIdentifier right)
        => !(left == right);

    internal string GetSddlForm()
    {
        var sidString = Value;

        var acct = WellKnownAccount.LookupBySid(sidString);
        if (acct?.SddlForm == null)
        {
            return sidString;
        }

        return acct.SddlForm;
    }

    internal static SecurityIdentifier ParseSddlForm(ReadOnlySpan<char> sddlForm, ref int pos)
    {
        if (sddlForm.Length - pos < 2)
        {
            throw new ArgumentException("Invalid SDDL string.", nameof(sddlForm));
        }

        string sid;
        int len;

        if (sddlForm.Slice(pos).StartsWith("S-".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            // Looks like a SID, try to parse it.
            var endPos = pos;

            var ch = char.ToUpperInvariant(sddlForm[endPos]);
            while (ch is 'S' or '-' or 'X'
                   or >= '0' and <= '9' or >= 'A' and <= 'F')
            {
                ++endPos;
                ch = char.ToUpperInvariant(sddlForm[endPos]);
            }

            if (ch == ':' && sddlForm[endPos - 1] == 'D')
            {
                endPos--;
            }

            sid = sddlForm.Slice(pos, endPos - pos).ToString();
            len = endPos - pos;
        }
        else
        {
            sid = sddlForm.Slice(pos, 2).ToString().ToUpperInvariant();
            len = 2;
        }

        var ret = new SecurityIdentifier(sid);
        pos += len;
        return ret;
    }

    private static byte[] ParseSddlForm(ReadOnlySpan<char> sddlForm)
    {
        var sid = sddlForm;

        // If only 2 characters long, can't be a full SID string - so assume
        // it's an attempted alias.  Do that conversion first.
        if (sid.Length == 2)
        {
            var acct = WellKnownAccount.LookupBySddlForm(sid);
            if (acct == null)
            {
                throw new ArgumentException(
                    $"Invalid SDDL string - unrecognized account: {sid.ToString()}",
                    nameof(sddlForm));
            }

            if (!acct.IsAbsolute)
            {
                throw new NotImplementedException(
                    $"Unable to convert account to SID: {acct.Name ?? sid.ToString()}");
            }

            return acct.Sid.buffer;
        }

        return ParseSddlForm(sid.ToString());
    }

    private static byte[] ParseSddlForm(string sddlForm)
    {
        var sid = sddlForm;

        // If only 2 characters long, can't be a full SID string - so assume
        // it's an attempted alias.  Do that conversion first.
        if (sid.Length == 2)
        {
            var acct = WellKnownAccount.LookupBySddlForm(sid);
            if (acct == null)
            {
                throw new ArgumentException(
                    $"Invalid SDDL string - unrecognized account: {sid}",
                    nameof(sddlForm));
            }

            if (!acct.IsAbsolute)
            {
                throw new NotImplementedException(
                    $"Unable to convert account to SID: {acct.Name ?? sid}");
            }

            sid = acct.SidStr;
        }

        var elements = sid.Split('-');
        var numSubAuthorities = elements.Length - 3;

        if (elements.Length < 3 || !elements[0].Equals("S", StringComparison.OrdinalIgnoreCase) || numSubAuthorities > 15)
        {
            throw new ArgumentException("Value was invalid.");
        }

        if (elements[1] != "1")
        {
            throw new ArgumentException("Only SIDs with revision 1 are supported");
        }

        var buffer = new byte[8 + (numSubAuthorities * 4)];
        buffer[0] = 1;
        buffer[1] = (byte)numSubAuthorities;

        if (!TryParseAuthority(elements[2], out var authority))
        {
            throw new ArgumentException("Value was invalid.");
        }

        buffer[2] = (byte)((authority >> 40) & 0xFF);
        buffer[3] = (byte)((authority >> 32) & 0xFF);
        buffer[4] = (byte)((authority >> 24) & 0xFF);
        buffer[5] = (byte)((authority >> 16) & 0xFF);
        buffer[6] = (byte)((authority >> 8) & 0xFF);
        buffer[7] = (byte)((authority >> 0) & 0xFF);

        for (var i = 0; i < numSubAuthorities; ++i)
        {
            if (!TryParseSubAuthority(elements[i + 3],
                out var subAuthority))
            {
                throw new ArgumentException("Value was invalid.");
            }

            // Note sub authorities little-endian!
            var offset = 8 + (i * 4);
            buffer[offset + 0] = (byte)(subAuthority >> 0);
            buffer[offset + 1] = (byte)(subAuthority >> 8);
            buffer[offset + 2] = (byte)(subAuthority >> 16);
            buffer[offset + 3] = (byte)(subAuthority >> 24);
        }

        return buffer;
    }

    private static bool TryParseAuthority(string s, out ulong result)
    {
        if (s.StartsWith("0X"))
        {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
            return ulong.TryParse(s.AsSpan(2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out result);
#else
            return ulong.TryParse(s.Substring(2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out result);
#endif
        }
        else
        {
            return ulong.TryParse(s, NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out result);
        }
    }

    private static bool TryParseSubAuthority(string s, out uint result)
    {
        if (s.StartsWith("0X"))
        {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
            return uint.TryParse(s.AsSpan(2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out result);
#else
            return uint.TryParse(s.Substring(2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out result);
#endif
        }
        else
        {
            return uint.TryParse(s, NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out result);
        }
    }
}