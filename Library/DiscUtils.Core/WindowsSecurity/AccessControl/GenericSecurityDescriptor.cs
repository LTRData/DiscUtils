using System;
using System.Globalization;
using System.Text;

namespace DiscUtils.Core.WindowsSecurity.AccessControl;

public abstract class GenericSecurityDescriptor
{
    public int BinaryLength
    {
        get
        {
            var len = 0x14;
            if (Owner != null)
            {
                len += Owner.BinaryLength;
            }

            if (Group != null)
            {
                len += Group.BinaryLength;
            }

            if (DaclPresent && !DaclIsUnmodifiedAefa)
            {
                len += InternalDacl.BinaryLength;
            }

            if (SaclPresent)
            {
                len += InternalSacl.BinaryLength;
            }

            return len;
        }
    }

    public abstract ControlFlags ControlFlags { get; }

    public abstract SecurityIdentifier Group { get; set; }

    public abstract SecurityIdentifier Owner { get; set; }

    public static byte Revision => 1;

    internal virtual GenericAcl InternalDacl => null;

    internal virtual GenericAcl InternalSacl => null;

    internal virtual byte InternalReservedField => 0;

    public void GetBinaryForm(byte[] binaryForm, int offset)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(binaryForm);
#else
        if (null == binaryForm)
        {
            throw new ArgumentNullException(nameof(binaryForm));
        }
#endif

        var binaryLength = BinaryLength;
        if (offset < 0 || offset > binaryForm.Length - binaryLength)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        var controlFlags = ControlFlags;
        if (DaclIsUnmodifiedAefa)
        {
            controlFlags &= ~ControlFlags.DiscretionaryAclPresent;
        }

        binaryForm[offset + 0x00] = Revision;
        binaryForm[offset + 0x01] = InternalReservedField;
        WriteUShort((ushort)controlFlags, binaryForm,
            offset + 0x02);

        // Skip 'offset' fields (will fill later)
        var pos = 0x14;

        if (Owner != null)
        {
            WriteInt(pos, binaryForm, offset + 0x04);
            Owner.GetBinaryForm(binaryForm, offset + pos);
            pos += Owner.BinaryLength;
        }
        else
        {
            WriteInt(0, binaryForm, offset + 0x04);
        }

        if (Group != null)
        {
            WriteInt(pos, binaryForm, offset + 0x08);
            Group.GetBinaryForm(binaryForm, offset + pos);
            pos += Group.BinaryLength;
        }
        else
        {
            WriteInt(0, binaryForm, offset + 0x08);
        }

        var sysAcl = InternalSacl;
        if (SaclPresent)
        {
            WriteInt(pos, binaryForm, offset + 0x0C);
            sysAcl.GetBinaryForm(binaryForm, offset + pos);
            pos += InternalSacl.BinaryLength;
        }
        else
        {
            WriteInt(0, binaryForm, offset + 0x0C);
        }

        var discAcl = InternalDacl;
        if (DaclPresent && !DaclIsUnmodifiedAefa)
        {
            WriteInt(pos, binaryForm, offset + 0x10);
            discAcl.GetBinaryForm(binaryForm, offset + pos);
            pos += InternalDacl.BinaryLength;
        }
        else
        {
            WriteInt(0, binaryForm, offset + 0x10);
        }
    }

    public string GetSddlForm(AccessControlSections includeSections)
    {
        var result = new StringBuilder();

        if ((includeSections & AccessControlSections.Owner) != 0
            && Owner != null)
        {
            result.AppendFormat(
                CultureInfo.InvariantCulture,
                "O:{0}", Owner.GetSddlForm());
        }

        if ((includeSections & AccessControlSections.Group) != 0
            && Group != null)
        {
            result.AppendFormat(
                CultureInfo.InvariantCulture,
                "G:{0}", Group.GetSddlForm());
        }

        if ((includeSections & AccessControlSections.Access) != 0
            && DaclPresent && !DaclIsUnmodifiedAefa)
        {
            result.AppendFormat(
                CultureInfo.InvariantCulture,
                "D:{0}",
                InternalDacl.GetSddlForm(ControlFlags,
                    true));
        }

        if ((includeSections & AccessControlSections.Audit) != 0
            && SaclPresent)
        {
            result.AppendFormat(
                CultureInfo.InvariantCulture,
                "S:{0}",
                InternalSacl.GetSddlForm(ControlFlags,
                    false));
        }

        return result.ToString();
    }

    public static bool IsSddlConversionSupported()
    {
        return true;
    }

    // See CommonSecurityDescriptor constructor regarding this persistence detail.
    internal virtual bool DaclIsUnmodifiedAefa => false;

    bool DaclPresent =>
        InternalDacl != null
        && (ControlFlags & ControlFlags.DiscretionaryAclPresent) != 0;

    bool SaclPresent =>
        InternalSacl != null
        && (ControlFlags & ControlFlags.SystemAclPresent) != 0;

    static void WriteUShort(ushort val, byte[] buffer, int offset)
    {
        buffer[offset] = (byte)val;
        buffer[offset + 1] = (byte)(val >> 8);
    }

    static void WriteInt(int val, byte[] buffer, int offset)
    {
        buffer[offset] = (byte)val;
        buffer[offset + 1] = (byte)(val >> 8);
        buffer[offset + 2] = (byte)(val >> 16);
        buffer[offset + 3] = (byte)(val >> 24);
    }

    public byte[] GetSecurityDescriptorBinaryForm()
    {
        var buffer = new byte[BinaryLength];
        GetBinaryForm(buffer, 0);
        return buffer;
    }
}