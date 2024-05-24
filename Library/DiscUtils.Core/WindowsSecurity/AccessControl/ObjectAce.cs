using DiscUtils.Streams;
using System;
using System.Globalization;

namespace DiscUtils.Core.WindowsSecurity.AccessControl;

public sealed class ObjectAce : QualifiedAce
{
    private Guid _objectAceType;
    private Guid _inheritedObjectType;
    
    public ObjectAceFlags ObjectAceFlags { get; set; }

    public ObjectAce(AceFlags aceFlags, AceQualifier qualifier,
                     int accessMask, SecurityIdentifier sid,
                     ObjectAceFlags flags, Guid type,
                     Guid inheritedType, bool isCallback,
                     byte[] opaque)
        : base(ConvertType(qualifier, isCallback), aceFlags, opaque)
    {
        AccessMask = accessMask;
        SecurityIdentifier = sid;
        ObjectAceFlags = flags;
        ObjectAceType = type;
        InheritedObjectAceType = inheritedType;
    }

    internal ObjectAce(AceType type, AceFlags flags, int accessMask,
                       SecurityIdentifier sid, ObjectAceFlags objFlags,
                       Guid objType, Guid inheritedType, byte[] opaque)
        : base(type, flags, opaque)
    {
        AccessMask = accessMask;
        SecurityIdentifier = sid;
        ObjectAceFlags = objFlags;
        ObjectAceType = objType;
        InheritedObjectAceType = inheritedType;
    }

    internal ObjectAce(byte[] binaryForm, int offset)
        : this(binaryForm.AsSpan(offset))
    {
    }

    internal ObjectAce(ReadOnlySpan<byte> binaryForm)
        : base(binaryForm)
    {
        int len = ReadUShort(binaryForm.Slice(2));
        var lenMinimum = 12 + SecurityIdentifier.MinBinaryLength;

        if (len > binaryForm.Length)
        {
            throw new ArgumentException("Invalid ACE - truncated", nameof(binaryForm));
        }

        if (len < lenMinimum)
        {
            throw new ArgumentException("Invalid ACE", nameof(binaryForm));
        }

        AccessMask = ReadInt(binaryForm.Slice(4));
        ObjectAceFlags = (ObjectAceFlags)ReadInt(binaryForm.Slice(8));

        if (ObjectAceTypePresent)
        {
            lenMinimum += 16;
        }

        if (InheritedObjectAceTypePresent)
        {
            lenMinimum += 16;
        }

        if (len < lenMinimum)
        {
            throw new ArgumentException("Invalid ACE", nameof(binaryForm));
        }

        var pos = 12;
        if (ObjectAceTypePresent)
        {
            ObjectAceType = ReadGuid(binaryForm.Slice(pos));
            pos += 16;
        }

        if (InheritedObjectAceTypePresent)
        {
            InheritedObjectAceType = ReadGuid(binaryForm.Slice(pos));
            pos += 16;
        }

        SecurityIdentifier = new SecurityIdentifier(binaryForm.Slice(pos));
        pos += SecurityIdentifier.BinaryLength;

        var opaqueLen = len - pos;
        if (opaqueLen > 0)
        {
            var opaque = binaryForm.Slice(pos, opaqueLen);
            SetOpaque(opaque);
        }
    }

    public override int BinaryLength
    {
        get
        {
            var length = 12 + SecurityIdentifier.BinaryLength + OpaqueLength;
            if (ObjectAceTypePresent)
            {
                length += 16;
            }

            if (InheritedObjectAceTypePresent)
            {
                length += 16;
            }

            return length;
        }
    }

    public Guid InheritedObjectAceType
    {
        get => _inheritedObjectType;
        set => _inheritedObjectType = value;
    }

    bool InheritedObjectAceTypePresent => 0 != (ObjectAceFlags & ObjectAceFlags.InheritedObjectAceTypePresent);

    public Guid ObjectAceType
    {
        get => _objectAceType;
        set => _objectAceType = value;
    }

    bool ObjectAceTypePresent => 0 != (ObjectAceFlags & ObjectAceFlags.ObjectAceTypePresent);

    public override void GetBinaryForm(Span<byte> binaryForm)
    {
        var offset = 0;
        var len = BinaryLength;
        binaryForm[offset++] = (byte)AceType;
        binaryForm[offset++] = (byte)AceFlags;
        WriteUShort((ushort)len, binaryForm.Slice(offset));
        offset += 2;
        WriteInt(AccessMask, binaryForm.Slice(offset));
        offset += 4;
        WriteInt((int)ObjectAceFlags, binaryForm.Slice(offset));
        offset += 4;

        if (0 != (ObjectAceFlags & ObjectAceFlags.ObjectAceTypePresent))
        {
            WriteGuid(ObjectAceType, binaryForm.Slice(offset));
            offset += 16;
        }

        if (0 != (ObjectAceFlags & ObjectAceFlags.InheritedObjectAceTypePresent))
        {
            WriteGuid(InheritedObjectAceType, binaryForm.Slice(offset));
            offset += 16;
        }

        SecurityIdentifier.GetBinaryForm(binaryForm.Slice(offset));
        offset += SecurityIdentifier.BinaryLength;

        var opaque = GetOpaque();
        if (opaque != null)
        {
            opaque.CopyTo(binaryForm.Slice(offset));
            offset += opaque.Length;
        }
    }

    public static int MaxOpaqueLength(bool isCallback) =>
        // Varies by platform?
        65423;

    internal override string GetSddlForm()
    {
        if (OpaqueLength != 0)
        {
            throw new NotImplementedException(
                "Unable to convert conditional ACEs to SDDL");
        }

        var objType = "";
        if ((ObjectAceFlags & ObjectAceFlags.ObjectAceTypePresent) != 0)
        {
            objType = _objectAceType.ToString("D");
        }

        var inhObjType = "";
        if ((ObjectAceFlags & ObjectAceFlags.InheritedObjectAceTypePresent) != 0)
        {
            inhObjType = _inheritedObjectType.ToString("D");
        }

        return $"({GetSddlAceType(AceType)};{GetSddlAceFlags(AceFlags)};{GetSddlAccessRights(AccessMask)};{objType};{inhObjType};{SecurityIdentifier.GetSddlForm()})";
    }

    private static AceType ConvertType(AceQualifier qualifier, bool isCallback)
    {
        switch (qualifier)
        {
            case AceQualifier.AccessAllowed:
                if (isCallback)
                {
                    return AceType.AccessAllowedCallbackObject;
                }
                else
                {
                    return AceType.AccessAllowedObject;
                }

            case AceQualifier.AccessDenied:
                if (isCallback)
                {
                    return AceType.AccessDeniedCallbackObject;
                }
                else
                {
                    return AceType.AccessDeniedObject;
                }

            case AceQualifier.SystemAlarm:
                if (isCallback)
                {
                    return AceType.SystemAlarmCallbackObject;
                }
                else
                {
                    return AceType.SystemAlarmObject;
                }

            case AceQualifier.SystemAudit:
                if (isCallback)
                {
                    return AceType.SystemAuditCallbackObject;
                }
                else
                {
                    return AceType.SystemAuditObject;
                }

            default:
                throw new ArgumentException($"Unrecognized ACE qualifier: {qualifier}", nameof(qualifier));
        }
    }

    private static void WriteGuid(Guid val, Span<byte> buffer)
    {
        EndianUtilities.WriteBytesLittleEndian(val, buffer);
    }

    private static Guid ReadGuid(ReadOnlySpan<byte> buffer)
    {
        return EndianUtilities.ToGuidLittleEndian(buffer);
    }
}