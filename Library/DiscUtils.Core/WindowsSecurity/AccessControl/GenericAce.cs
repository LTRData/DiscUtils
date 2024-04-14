using DiscUtils.Streams.Compatibility;
using LTRData.Extensions.Buffers;
using LTRData.Extensions.Split;
using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DiscUtils.Core.WindowsSecurity.AccessControl;

public abstract class GenericAce
{
    public AceFlags AceFlags { get; set; }

    public AceType AceType { get; }

    public AuditFlags AuditFlags
    {
        get
        {
            var ret = AuditFlags.None;
            if ((AceFlags & AceFlags.SuccessfulAccess) != 0)
            {
                ret |= AuditFlags.Success;
            }

            if ((AceFlags & AceFlags.FailedAccess) != 0)
            {
                ret |= AuditFlags.Failure;
            }

            return ret;
        }
    }

    public abstract int BinaryLength { get; }

    public InheritanceFlags InheritanceFlags
    {
        get
        {
            var ret = InheritanceFlags.None;
            if ((AceFlags & AceFlags.ObjectInherit) != 0)
            {
                ret |= InheritanceFlags.ObjectInherit;
            }

            if ((AceFlags & AceFlags.ContainerInherit) != 0)
            {
                ret |= InheritanceFlags.ContainerInherit;
            }

            return ret;
        }
    }

    public bool IsInherited => (AceFlags & AceFlags.Inherited) != AceFlags.None;

    public PropagationFlags PropagationFlags
    {
        get
        {
            var ret = PropagationFlags.None;
            if ((AceFlags & AceFlags.InheritOnly) != 0)
            {
                ret |= PropagationFlags.InheritOnly;
            }

            if ((AceFlags & AceFlags.NoPropagateInherit) != 0)
            {
                ret |= PropagationFlags.NoPropagateInherit;
            }

            return ret;
        }
    }

    internal GenericAce(AceType type, AceFlags flags)
    {
        if (type > AceType.MaxDefinedAceType)
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        AceType = type;
        AceFlags = flags;
    }

    internal GenericAce(ReadOnlySpan<byte> binaryForm)
    {
        if (binaryForm.IsEmpty)
        {
            throw new ArgumentNullException(nameof(binaryForm));
        }

        if (2 > binaryForm.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(binaryForm), binaryForm.Length, "Binary length out of range");
        }

        AceType = (AceType)binaryForm[0];
        AceFlags = (AceFlags)binaryForm[1];
    }

    public GenericAce Copy()
    {
        Span<byte> buffer = stackalloc byte[BinaryLength];
        GetBinaryForm(buffer);
        return CreateFromBinaryForm(buffer);
    }

    public static GenericAce CreateFromBinaryForm(byte[] binaryForm, int offset) =>
        CreateFromBinaryForm(binaryForm.AsSpan(offset));

    public static bool TryCreateFromBinaryForm(byte[] binaryForm, int offset, out GenericAce genericAce) =>
        TryCreateFromBinaryForm(binaryForm.AsSpan(offset), out genericAce);

    public static GenericAce CreateFromBinaryForm(ReadOnlySpan<byte> binaryForm)
    {
        if (binaryForm == null)
        {
            throw new ArgumentNullException(nameof(binaryForm));
        }

        if (1 > binaryForm.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(binaryForm), binaryForm.Length, "Binary length out of range");
        }

        var type = (AceType)binaryForm[0];
        if (IsObjectType(type))
        {
            return new ObjectAce(binaryForm);
        }
        else
        {
            return new CommonAce(binaryForm);
        }
    }

    public static bool TryCreateFromBinaryForm(ReadOnlySpan<byte> binaryForm, out GenericAce genericAce)
    {
        genericAce = null;

        if (binaryForm == null || 1 > binaryForm.Length)
        {
            return false;
        }

        try
        {
            var type = (AceType)binaryForm[0];
            if (IsObjectType(type))
            {
                genericAce = new ObjectAce(binaryForm);
            }
            else
            {
                genericAce = new CommonAce(binaryForm);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public sealed override bool Equals(object o) => this == (o as GenericAce);

    public void GetBinaryForm(byte[] binaryForm, int offset) => GetBinaryForm(binaryForm.AsSpan(offset));

    public abstract void GetBinaryForm(Span<byte> binaryForm);

    public sealed override int GetHashCode()
    {
        Span<byte> buffer = stackalloc byte[BinaryLength];

        GetBinaryForm(buffer);

        var code = new HashCode();
        for (var i = 0; i < buffer.Length; ++i)
        {
            code.Add(buffer[i]);
        }

        return code.ToHashCode();
    }

    public static bool Equals(GenericAce left, GenericAce right)
    {
        if (left is null)
        {
            return right is null;
        }

        if (right is null)
        {
            return false;
        }

        var leftLen = left.BinaryLength;
        var rightLen = right.BinaryLength;
        if (leftLen != rightLen)
        {
            return false;
        }

        Span<byte> leftBuffer = stackalloc byte[leftLen];
        Span<byte> rightBuffer = stackalloc byte[rightLen];

        left.GetBinaryForm(leftBuffer);
        right.GetBinaryForm(rightBuffer);

        return leftBuffer.SequenceEqual(rightBuffer);
    }

    public static bool operator ==(GenericAce left, GenericAce right) => Equals(left, right);

    public static bool operator !=(GenericAce left, GenericAce right) => !Equals(left, right);

    internal abstract string GetSddlForm();

    internal static GenericAce CreateFromSddlForm(ReadOnlySpan<char> sddlForm, ref int pos)
    {
        if (sddlForm[pos] != '(')
        {
            throw new ArgumentException("Invalid SDDL string.", nameof(sddlForm));
        }

        sddlForm = sddlForm.Slice(pos + 1);

        var endPos = sddlForm.IndexOf(')');
        if (endPos < 0)
        {
            throw new ArgumentException("Invalid SDDL string.", nameof(sddlForm));
        }

        var count = endPos;
        var elementsStr = sddlForm.Slice(0, count).ToString();
        elementsStr = elementsStr.ToUpperInvariant();
        var elements = elementsStr.AsMemory().Split(';').ToArray();
        if (elements.Length != 6)
        {
            throw new ArgumentException("Invalid SDDL string.", nameof(sddlForm));
        }

        var objFlags = ObjectAceFlags.None;

        var type = ParseSddlAceType(elements[0].ToString());

        var flags = ParseSddlAceFlags(elements[1].ToString());

        var accessMask = ParseSddlAccessRights(elements[2].Span);

        var objectType = Guid.Empty;
        if (!elements[3].IsEmpty)
        {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
            objectType = Guid.Parse(elements[3].Span);
#else
            objectType = Guid.Parse(elements[3].ToString());
#endif
            objFlags |= ObjectAceFlags.ObjectAceTypePresent;
        }

        var inhObjectType = Guid.Empty;
        if (!elements[4].IsEmpty)
        {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
            inhObjectType = Guid.Parse(elements[4].Span);
#else
            inhObjectType = Guid.Parse(elements[4].ToString());
#endif
            objFlags |= ObjectAceFlags.InheritedObjectAceTypePresent;
        }

        var sid
            = new SecurityIdentifier(elements[5].Span);

        if (type is AceType.AccessAllowedCallback
            or AceType.AccessDeniedCallback)
        {
            throw new NotImplementedException("Conditional ACEs not supported");
        }

        pos += endPos + 2;

        if (IsObjectType(type))
        {
            return new ObjectAce(type, flags, accessMask, sid, objFlags, objectType, inhObjectType, null);
        }
        else
        {
            if (objFlags != ObjectAceFlags.None)
            {
                throw new ArgumentException("Invalid SDDL string.", nameof(sddlForm));
            }

            return new CommonAce(type, flags, accessMask, sid, null);
        }
    }

    private static bool IsObjectType(AceType type)
    {
        return type is AceType.AccessAllowedCallbackObject
               or AceType.AccessAllowedObject
               or AceType.AccessDeniedCallbackObject
               or AceType.AccessDeniedObject
               or AceType.SystemAlarmCallbackObject
               or AceType.SystemAlarmObject
               or AceType.SystemAuditCallbackObject
               or AceType.SystemAuditObject;
    }

    internal static string GetSddlAceType(AceType type)
    {
        return type switch
        {
            AceType.AccessAllowed => "A",
            AceType.AccessDenied => "D",
            AceType.AccessAllowedObject => "OA",
            AceType.AccessDeniedObject => "OD",
            AceType.SystemAudit => "AU",
            AceType.SystemAlarm => "AL",
            AceType.SystemAuditObject => "OU",
            AceType.SystemAlarmObject => "OL",
            AceType.AccessAllowedCallback => "XA",
            AceType.AccessDeniedCallback => "XD",
            _ => throw new ArgumentException($"Unable to convert to SDDL ACE type: {type}", nameof(type)),
        };
    }

    private static AceType ParseSddlAceType(string type)
    {
        return type switch
        {
            "A" => AceType.AccessAllowed,
            "D" => AceType.AccessDenied,
            "OA" => AceType.AccessAllowedObject,
            "OD" => AceType.AccessDeniedObject,
            "AU" => AceType.SystemAudit,
            "AL" => AceType.SystemAlarm,
            "OU" => AceType.SystemAuditObject,
            "OL" => AceType.SystemAlarmObject,
            "XA" => AceType.AccessAllowedCallback,
            "XD" => AceType.AccessDeniedCallback,
            _ => throw new ArgumentException($"Unable to convert SDDL to ACE type: {type}", nameof(type)),
        };
    }

    internal static string GetSddlAceFlags(AceFlags flags)
    {
        var result = new StringBuilder();
        if ((flags & AceFlags.ObjectInherit) != 0)
        {
            result.Append("OI");
        }

        if ((flags & AceFlags.ContainerInherit) != 0)
        {
            result.Append("CI");
        }

        if ((flags & AceFlags.NoPropagateInherit) != 0)
        {
            result.Append("NP");
        }

        if ((flags & AceFlags.InheritOnly) != 0)
        {
            result.Append("IO");
        }

        if ((flags & AceFlags.Inherited) != 0)
        {
            result.Append("ID");
        }

        if ((flags & AceFlags.SuccessfulAccess) != 0)
        {
            result.Append("SA");
        }

        if ((flags & AceFlags.FailedAccess) != 0)
        {
            result.Append("FA");
        }

        return result.ToString();
    }

    private static AceFlags ParseSddlAceFlags(string flags)
    {
        var ret = AceFlags.None;

        var pos = 0;
        while (pos < flags.Length - 1)
        {
            var flag = flags.Substring(pos, 2);
            ret |= flag switch
            {
                "CI" => AceFlags.ContainerInherit,
                "OI" => AceFlags.ObjectInherit,
                "NP" => AceFlags.NoPropagateInherit,
                "IO" => AceFlags.InheritOnly,
                "ID" => AceFlags.Inherited,
                "SA" => AceFlags.SuccessfulAccess,
                "FA" => AceFlags.FailedAccess,
                _ => throw new ArgumentException("Invalid SDDL string.", nameof(flags)),
            };
            pos += 2;
        }

        if (pos != flags.Length)
        {
            throw new ArgumentException("Invalid SDDL string.", nameof(flags));
        }

        return ret;
    }

    private static int ParseSddlAccessRights(ReadOnlySpan<char> accessMask)
    {
        if (accessMask.StartsWith("0X".AsSpan(), StringComparison.Ordinal))
        {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
            return int.Parse(accessMask.Slice(2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture);
#else
            return int.Parse(accessMask.Slice(2).ToString(),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture);
#endif
        }
        else if (char.IsDigit(accessMask[0]))
        {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
            return int.Parse(accessMask,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture);
#else
            return int.Parse(accessMask.ToString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture);
#endif
        }
        else
        {
            return ParseSddlAliasRights(accessMask);
        }
    }

    private static int ParseSddlAliasRights(ReadOnlySpan<char> accessMask)
    {
        var ret = 0;

        var pos = 0;
        while (pos < accessMask.Length - 1)
        {
            var flag = accessMask.Slice(pos, 2);
            var right = SddlAccessRight.LookupByName(flag)
                ?? throw new ArgumentException("Invalid SDDL string.", nameof(accessMask));

            ret |= right.Value;
            pos += 2;
        }

        if (pos != accessMask.Length)
        {
            throw new ArgumentException("Invalid SDDL string.", nameof(accessMask));
        }

        return ret;
    }

    internal static ushort ReadUShort(byte[] buffer, int offset)
    {
        return (ushort)((((int)buffer[offset + 0]) << 0)
                        | (((int)buffer[offset + 1]) << 8));
    }

    internal static int ReadInt(byte[] buffer, int offset)
    {
        return (((int)buffer[offset + 0]) << 0)
               | (((int)buffer[offset + 1]) << 8)
               | (((int)buffer[offset + 2]) << 16)
               | (((int)buffer[offset + 3]) << 24);
    }

    internal static ushort ReadUShort(ReadOnlySpan<byte> buffer)
    {
        return (ushort)((((int)buffer[0]) << 0)
                        | (((int)buffer[1]) << 8));
    }

    internal static int ReadInt(ReadOnlySpan<byte> buffer)
    {
        return (((int)buffer[0]) << 0)
               | (((int)buffer[1]) << 8)
               | (((int)buffer[2]) << 16)
               | (((int)buffer[3]) << 24);
    }

    internal static void WriteInt(int val, byte[] buffer, int offset)
    {
        buffer[offset] = (byte)val;
        buffer[offset + 1] = (byte)(val >> 8);
        buffer[offset + 2] = (byte)(val >> 16);
        buffer[offset + 3] = (byte)(val >> 24);
    }

    internal static void WriteInt(int val, Span<byte> buffer)
    {
        buffer[0] = (byte)val;
        buffer[1] = (byte)(val >> 8);
        buffer[2] = (byte)(val >> 16);
        buffer[3] = (byte)(val >> 24);
    }

    internal static void WriteUShort(ushort val, byte[] buffer,
                                     int offset)
    {
        buffer[offset] = (byte)val;
        buffer[offset + 1] = (byte)(val >> 8);
    }

    internal static void WriteUShort(ushort val, Span<byte> buffer)
    {
        buffer[0] = (byte)val;
        buffer[1] = (byte)(val >> 8);
    }
}