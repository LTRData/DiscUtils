using DiscUtils.Streams;
using System;

namespace DiscUtils.Core.WindowsSecurity.AccessControl;

public class RawSecurityDescriptor : GenericSecurityDescriptor
{
    private ControlFlags _controlFlags;

    internal override GenericAcl InternalDacl => DiscretionaryAcl;
    internal override GenericAcl InternalSacl => SystemAcl;
    internal override byte InternalReservedField => ResourceManagerControl;
    
    public override ControlFlags ControlFlags => _controlFlags;

    public RawAcl DiscretionaryAcl { get; set; }
    public override SecurityIdentifier Group { get; set; }
    public override SecurityIdentifier Owner { get; set; }
    public byte ResourceManagerControl { get; set; }
    public RawAcl SystemAcl { get; set; }
    
    public RawSecurityDescriptor(string sddlForm)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(sddlForm);
#else
        if (sddlForm == null)
        {
            throw new ArgumentNullException(nameof(sddlForm));
        }
#endif

        SetSddlForm(sddlForm.Replace(" ", ""));

        _controlFlags |= ControlFlags.SelfRelative;
    }

    public RawSecurityDescriptor(byte[] binaryForm, int offset)
        : this(binaryForm.AsSpan(offset))
    {
    }

    private RawSecurityDescriptor() { }

    public static bool TryParse(byte[] binaryForm, int offset, out RawSecurityDescriptor securityDescriptor) =>
        TryParse(binaryForm.AsSpan(offset), out securityDescriptor);

    public RawSecurityDescriptor(ReadOnlySpan<byte> binaryForm)
    {
        if (binaryForm.Length < 0x14)
        {
            throw new ArgumentOutOfRangeException(nameof(binaryForm), binaryForm.Length, "Binary length out of range");
        }

        if (binaryForm[0] != 1)
        {
            throw new ArgumentException("Unrecognized Security Descriptor revision.", nameof(binaryForm));
        }

        ResourceManagerControl = binaryForm[0x01];
        _controlFlags = (ControlFlags)EndianUtilities.ToUInt16LittleEndian(binaryForm.Slice(0x02));

        var ownerPos = EndianUtilities.ToInt32LittleEndian(binaryForm.Slice(0x04));
        var groupPos = EndianUtilities.ToInt32LittleEndian(binaryForm.Slice(0x08));
        var saclPos = EndianUtilities.ToInt32LittleEndian(binaryForm.Slice(0x0C));
        var daclPos = EndianUtilities.ToInt32LittleEndian(binaryForm.Slice(0x10));

        if (ownerPos != 0)
        {
            Owner = new SecurityIdentifier(binaryForm.Slice(ownerPos));
        }

        if (groupPos != 0)
        {
            Group = new SecurityIdentifier(binaryForm.Slice(groupPos));
        }

        if (saclPos != 0)
        {
            SystemAcl = new RawAcl(binaryForm.Slice(saclPos));
        }

        if (daclPos != 0)
        {
            DiscretionaryAcl = new RawAcl(binaryForm.Slice(daclPos));
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> binaryForm, out RawSecurityDescriptor securityDescriptor)
    {
        securityDescriptor = null;

        if (binaryForm == null || 0x14 > binaryForm.Length || binaryForm[0] != 1)
        {
            return false;
        }

        var sd = new RawSecurityDescriptor
        {
            ResourceManagerControl = binaryForm[0x01],
            _controlFlags = (ControlFlags)EndianUtilities.ToUInt16LittleEndian(binaryForm.Slice(0x02))
        };

        var ownerPos = EndianUtilities.ToInt32LittleEndian(binaryForm.Slice(0x04));
        var groupPos = EndianUtilities.ToInt32LittleEndian(binaryForm.Slice(0x08));
        var saclPos = EndianUtilities.ToInt32LittleEndian(binaryForm.Slice(0x0C));
        var daclPos = EndianUtilities.ToInt32LittleEndian(binaryForm.Slice(0x10));

        if (ownerPos != 0)
        {
            if (!SecurityIdentifier.TryParse(binaryForm.Slice(ownerPos), out var owner))
            {
                return false;
            }

            sd.Owner = owner;
        }

        if (groupPos != 0)
        {
            if (!SecurityIdentifier.TryParse(binaryForm.Slice(groupPos), out var group))
            {
                return false;
            }

            sd.Group = group;
        }

        if (saclPos != 0)
        {
            if (!RawAcl.TryParse(binaryForm.Slice(saclPos), out var systemAcl))
            {
                return false;
            }

            sd.SystemAcl = systemAcl;
        }

        if (daclPos != 0)
        {
            if (!RawAcl.TryParse(binaryForm.Slice(daclPos), out var discretionaryAcl))
            {
                return false;
            }

            sd.DiscretionaryAcl = discretionaryAcl;
        }

        securityDescriptor = sd;
        return true;
    }

    public RawSecurityDescriptor(ControlFlags flags,
                                 SecurityIdentifier owner,
                                 SecurityIdentifier group,
                                 RawAcl systemAcl,
                                 RawAcl discretionaryAcl)
    {
        _controlFlags = flags;
        Owner = owner;
        Group = group;
        SystemAcl = systemAcl;
        DiscretionaryAcl = discretionaryAcl;
    }

    public void SetSddlForm(string sddlForm)
        => SetSddlForm(sddlForm.AsSpan());

    public void SetSddlForm(ReadOnlySpan<char> sddlForm)
    {
        var flags = ControlFlags.None;

        var pos = 0;
        while (pos < sddlForm.Length - 2)
        {
            switch (sddlForm.Slice(pos, 2).ToString())
            {
                case "O:":
                    pos += 2;
                    Owner = SecurityIdentifier.ParseSddlForm(sddlForm, ref pos);
                    break;

                case "G:":
                    pos += 2;
                    Group = SecurityIdentifier.ParseSddlForm(sddlForm, ref pos);
                    break;

                case "D:":
                    pos += 2;
                    DiscretionaryAcl = RawAcl.ParseSddlForm(sddlForm, true, ref flags, ref pos);
                    flags |= ControlFlags.DiscretionaryAclPresent;
                    break;

                case "S:":
                    pos += 2;
                    SystemAcl = RawAcl.ParseSddlForm(sddlForm, false, ref flags, ref pos);
                    flags |= ControlFlags.SystemAclPresent;
                    break;
                default:

                    throw new ArgumentException("Invalid SDDL.", nameof(sddlForm));
            }
        }

        if (pos != sddlForm.Length)
        {
            throw new ArgumentException("Invalid SDDL.", nameof(sddlForm));
        }

        SetFlags(flags);
    }

    public void SetFlags(ControlFlags flags) => _controlFlags = flags | ControlFlags.SelfRelative;
}