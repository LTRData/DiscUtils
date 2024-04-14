using System;
using System.Globalization;

namespace DiscUtils.Core.WindowsSecurity.AccessControl;

public sealed class CommonAce : QualifiedAce
{
    public override int BinaryLength => 8 + SecurityIdentifier.BinaryLength
                                          + OpaqueLength;
    
    public CommonAce(AceFlags flags, AceQualifier qualifier,
                     int accessMask, SecurityIdentifier sid,
                     bool isCallback, byte[] opaque)
        : base(ConvertType(qualifier, isCallback),
            flags,
            opaque)
    {
        AccessMask = accessMask;
        SecurityIdentifier = sid;
    }

    internal CommonAce(AceType type, AceFlags flags, int accessMask,
                       SecurityIdentifier sid, byte[] opaque)
        : base(type, flags, opaque)
    {
        AccessMask = accessMask;
        SecurityIdentifier = sid;
    }

    internal CommonAce(byte[] binaryForm, int offset)
        : this(binaryForm.AsSpan(offset))
    {
    }

    internal CommonAce(ReadOnlySpan<byte> binaryForm)
        : base(binaryForm)
    {
        int len = ReadUShort(binaryForm.Slice(2));
        if (len > binaryForm.Length)
        {
            throw new ArgumentException("Invalid ACE - truncated", nameof(binaryForm));
        }

        if (len < 8 + SecurityIdentifier.MinBinaryLength)
        {
            throw new ArgumentException("Invalid ACE", nameof(binaryForm));
        }

        AccessMask = ReadInt(binaryForm.Slice(4));
        SecurityIdentifier = new SecurityIdentifier(binaryForm.Slice(8));

        var opaqueLen = len - (8 + SecurityIdentifier.BinaryLength);
        if (opaqueLen > 0)
        {
            var opaque = binaryForm.Slice(8 + SecurityIdentifier.BinaryLength, opaqueLen);
            SetOpaque(opaque);
        }
    }

    public override void GetBinaryForm(Span<byte> binaryForm)
    {
        var len = BinaryLength;
        binaryForm[0] = (byte)AceType;
        binaryForm[1] = (byte)AceFlags;
        WriteUShort((ushort)len, binaryForm.Slice(2));
        WriteInt(AccessMask, binaryForm.Slice(4));

        SecurityIdentifier.GetBinaryForm(binaryForm.Slice(8));

        var opaque = GetOpaque();
        opaque?.CopyTo(binaryForm.Slice(8 + SecurityIdentifier.BinaryLength));
    }

    public static int MaxOpaqueLength(bool isCallback)
    {
        // Varies by platform?
        return 65459;
    }

    internal override string GetSddlForm()
    {
        if (OpaqueLength != 0)
        {
            throw new NotImplementedException(
                "Unable to convert conditional ACEs to SDDL");
        }

        return $"({GetSddlAceType(AceType)};{GetSddlAceFlags(AceFlags)};{GetSddlAccessRights(AccessMask)};;;{SecurityIdentifier.GetSddlForm()})";
    }

    private static AceType ConvertType(AceQualifier qualifier,
                                       bool isCallback)
    {
        switch (qualifier)
        {
            case AceQualifier.AccessAllowed:
                if (isCallback)
                {
                    return AceType.AccessAllowedCallback;
                }
                else
                {
                    return AceType.AccessAllowed;
                }

            case AceQualifier.AccessDenied:
                if (isCallback)
                {
                    return AceType.AccessDeniedCallback;
                }
                else
                {
                    return AceType.AccessDenied;
                }

            case AceQualifier.SystemAlarm:
                if (isCallback)
                {
                    return AceType.SystemAlarmCallback;
                }
                else
                {
                    return AceType.SystemAlarm;
                }

            case AceQualifier.SystemAudit:
                if (isCallback)
                {
                    return AceType.SystemAuditCallback;
                }
                else
                {
                    return AceType.SystemAudit;
                }

            default:
                throw new ArgumentException($"Unrecognized ACE qualifier: {qualifier}", nameof(qualifier));
        }
    }
}