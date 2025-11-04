using System;

namespace DiscUtils.Core.WindowsSecurity.AccessControl;

public abstract class QualifiedAce : KnownAce
{
    private byte[]? _opaque;

    internal QualifiedAce(AceType type, AceFlags flags, byte[]? opaque)
        : base(type, flags)
    {
        SetOpaque(opaque);
    }

    internal QualifiedAce(AceType type, AceFlags flags, ReadOnlySpan<byte> opaque)
        : base(type, flags)
    {
        SetOpaque(opaque);
    }

    internal QualifiedAce(ReadOnlySpan<byte> binaryForm)
        : base(binaryForm) { }

    internal QualifiedAce(byte[] binaryForm, int offset)
        : this(binaryForm.AsSpan(offset)) { }

    public AceQualifier AceQualifier => AceType switch
    {
        AceType.AccessAllowed or AceType.AccessAllowedCallback or AceType.AccessAllowedCallbackObject or AceType.AccessAllowedCompound or AceType.AccessAllowedObject => AceQualifier.AccessAllowed,
        AceType.AccessDenied or AceType.AccessDeniedCallback or AceType.AccessDeniedCallbackObject or AceType.AccessDeniedObject => AceQualifier.AccessDenied,
        AceType.SystemAlarm or AceType.SystemAlarmCallback or AceType.SystemAlarmCallbackObject or AceType.SystemAlarmObject => AceQualifier.SystemAlarm,
        AceType.SystemAudit or AceType.SystemAuditCallback or AceType.SystemAuditCallbackObject or AceType.SystemAuditObject => AceQualifier.SystemAudit,
        _ => throw new ArgumentException($"Unrecognized ACE type: {AceType}"),
    };

    public bool IsCallback =>
        AceType is AceType.AccessAllowedCallback
        or AceType.AccessAllowedCallbackObject
        or AceType.AccessDeniedCallback
        or AceType.AccessDeniedCallbackObject
        or AceType.SystemAlarmCallback
        or AceType.SystemAlarmCallbackObject
        or AceType.SystemAuditCallback
        or AceType.SystemAuditCallbackObject;

    public int OpaqueLength => _opaque == null ? 0 : _opaque.Length;

    public byte[]? GetOpaque() => (byte[]?)_opaque?.Clone();

    public void SetOpaque(ReadOnlySpan<byte> opaque) => _opaque = opaque.IsEmpty ? null : opaque.ToArray();
}