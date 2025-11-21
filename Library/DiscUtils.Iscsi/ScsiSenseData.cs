using System;
using System.Collections.Generic;

namespace DiscUtils.Iscsi;

/// <summary>
/// SCSI Sense Key values (SPC).
/// </summary>
public enum ScsiSenseKey : byte
{
    NoSense = 0x0,
    RecoveredError = 0x1,
    NotReady = 0x2,
    MediumError = 0x3,
    HardwareError = 0x4,
    IllegalRequest = 0x5,
    UnitAttention = 0x6,
    DataProtect = 0x7,
    BlankCheck = 0x8,
    VendorSpecific = 0x9,
    CopyAborted = 0xA,
    AbortedCommand = 0xB,
    VolumeOverflow = 0xD,
    Miscompare = 0xE
    // 0xC, 0xF are reserved / obsolete
}

/// <summary>
/// Some common SenseKey/ASC/ASCQ combinations (KCQ) with a descriptive name.
/// The numeric value is packed as: (SenseKey &lt;&lt; 16) | (ASC &lt;&lt; 8) | ASCQ.
/// </summary>
public enum ScsiKnownSenseCode : int
{
    // No sense / informational
    NoSense_NoAdditionalInfo = (0x0 << 16) | (0x00 << 8) | 0x00,

    // Medium errors
    MediumError_UnrecoveredReadError = (0x3 << 16) | (0x11 << 8) | 0x00,
    MediumError_WriteErrorAutoReallocFailed = (0x3 << 16) | (0x0C << 8) | 0x02,

    // Hardware errors
    HardwareError_InternalTargetFailure = (0x4 << 16) | (0x44 << 8) | 0x00,

    // Illegal request
    IllegalRequest_InvalidCommandOperationCode = (0x5 << 16) | (0x20 << 8) | 0x00,
    IllegalRequest_InvalidFieldInCdb = (0x5 << 16) | (0x24 << 8) | 0x00,
    IllegalRequest_LbaOutOfRange = (0x5 << 16) | (0x21 << 8) | 0x00,

    // Not ready / unit attention
    NotReady_LogicalUnitNotReady_CauseNotReportable = (0x2 << 16) | (0x04 << 8) | 0x00,
    UnitAttention_PowerOnResetOrBusResetOccurred = (0x6 << 16) | (0x29 << 8) | 0x00,
    UnitAttention_ParametersChanged = (0x6 << 16) | (0x2A << 8) | 0x00,

    // Data protect / write protected
    DataProtect_WriteProtected = (0x7 << 16) | (0x27 << 8) | 0x00,

    // Aborted command
    AbortedCommand_ScsiParityError = (0xB << 16) | (0x47 << 8) | 0x00,
}

/// <summary>
/// Parsed representation of SCSI sense data.
/// </summary>
public sealed class ScsiSenseData
{
    /// <summary>Raw response code (low 7 bits of byte 0).</summary>
    public byte ResponseCode { get; }

    /// <summary>True if this is current error information (as opposed to deferred).</summary>
    public bool IsCurrentError { get; }

    /// <summary>True if this is descriptor-format sense data (0x72/0x73).</summary>
    public bool IsDescriptorFormat { get; }

    /// <summary>The SCSI sense key.</summary>
    public ScsiSenseKey SenseKey { get; }

    /// <summary>Additional Sense Code (ASC).</summary>
    public byte AdditionalSenseCode { get; }

    /// <summary>Additional Sense Code Qualifier (ASCQ).</summary>
    public byte AdditionalSenseCodeQualifier { get; }

    /// <summary>LBA or similar "information" value (if present and applicable).</summary>
    public uint? Information { get; }

    /// <summary>A known combined sense code, if this KCQ was recognized.</summary>
    public ScsiKnownSenseCode? KnownSenseCode { get; }

    /// <summary>Human-readable description of the sense information.</summary>
    public string Description { get; }

    public bool IndicatesRetryRequired
        => SenseKey == ScsiSenseKey.UnitAttention
        && AdditionalSenseCode == 0x29
        && AdditionalSenseCodeQualifier == 0x00;

    internal ScsiSenseData(
        byte responseCode,
        bool isCurrent,
        bool isDescriptor,
        ScsiSenseKey senseKey,
        byte asc,
        byte ascq,
        uint? information,
        ScsiKnownSenseCode? known,
        string description)
    {
        ResponseCode = responseCode;
        IsCurrentError = isCurrent;
        IsDescriptorFormat = isDescriptor;
        SenseKey = senseKey;
        AdditionalSenseCode = asc;
        AdditionalSenseCodeQualifier = ascq;
        Information = information;
        KnownSenseCode = known;
        Description = description;
    }

    public override string ToString()
    {
        if (KnownSenseCode is ScsiKnownSenseCode known)
        {
            return $"{known} ({Description}) [SK={SenseKey}, ASC=0x{AdditionalSenseCode:X2}, ASCQ=0x{AdditionalSenseCodeQualifier:X2}]";
        }

        return $"{Description} [SK={SenseKey}, ASC=0x{AdditionalSenseCode:X2}, ASCQ=0x{AdditionalSenseCodeQualifier:X2}]";
    }
}

/// <summary>
/// Parser for SCSI sense data (fixed or descriptor format) into typed enums and descriptions.
/// </summary>
public static class ScsiSenseParser
{
    // Map known KCQ triples -> ScsiKnownSenseCode
    private static readonly Dictionary<(ScsiSenseKey key, byte asc, byte ascq), ScsiKnownSenseCode> KnownMap
        = new()
        {
            { (ScsiSenseKey.NoSense, 0x00, 0x00), ScsiKnownSenseCode.NoSense_NoAdditionalInfo },

            { ScsiTriple(ScsiKnownSenseCode.MediumError_UnrecoveredReadError), ScsiKnownSenseCode.MediumError_UnrecoveredReadError },
            { ScsiTriple(ScsiKnownSenseCode.MediumError_WriteErrorAutoReallocFailed), ScsiKnownSenseCode.MediumError_WriteErrorAutoReallocFailed },

            { ScsiTriple(ScsiKnownSenseCode.HardwareError_InternalTargetFailure), ScsiKnownSenseCode.HardwareError_InternalTargetFailure },

            { ScsiTriple(ScsiKnownSenseCode.IllegalRequest_InvalidCommandOperationCode), ScsiKnownSenseCode.IllegalRequest_InvalidCommandOperationCode },
            { ScsiTriple(ScsiKnownSenseCode.IllegalRequest_InvalidFieldInCdb), ScsiKnownSenseCode.IllegalRequest_InvalidFieldInCdb },
            { ScsiTriple(ScsiKnownSenseCode.IllegalRequest_LbaOutOfRange), ScsiKnownSenseCode.IllegalRequest_LbaOutOfRange },

            { ScsiTriple(ScsiKnownSenseCode.NotReady_LogicalUnitNotReady_CauseNotReportable), ScsiKnownSenseCode.NotReady_LogicalUnitNotReady_CauseNotReportable },
            { ScsiTriple(ScsiKnownSenseCode.UnitAttention_PowerOnResetOrBusResetOccurred), ScsiKnownSenseCode.UnitAttention_PowerOnResetOrBusResetOccurred },
            { ScsiTriple(ScsiKnownSenseCode.UnitAttention_ParametersChanged), ScsiKnownSenseCode.UnitAttention_ParametersChanged },

            { ScsiTriple(ScsiKnownSenseCode.DataProtect_WriteProtected), ScsiKnownSenseCode.DataProtect_WriteProtected },

            { ScsiTriple(ScsiKnownSenseCode.AbortedCommand_ScsiParityError), ScsiKnownSenseCode.AbortedCommand_ScsiParityError },
        };

    // Human-readable descriptions for known codes
    private static readonly Dictionary<ScsiKnownSenseCode, string> KnownDescriptions
        = new()
        {
            { ScsiKnownSenseCode.NoSense_NoAdditionalInfo, "No specific sense information" },

            { ScsiKnownSenseCode.MediumError_UnrecoveredReadError, "Unrecovered read error on medium" },
            { ScsiKnownSenseCode.MediumError_WriteErrorAutoReallocFailed, "Write error; automatic sector reallocation failed" },

            { ScsiKnownSenseCode.HardwareError_InternalTargetFailure, "Internal target failure (hardware error)" },

            { ScsiKnownSenseCode.IllegalRequest_InvalidCommandOperationCode, "Invalid command operation code" },
            { ScsiKnownSenseCode.IllegalRequest_InvalidFieldInCdb, "Invalid field in CDB" },
            { ScsiKnownSenseCode.IllegalRequest_LbaOutOfRange, "Logical block address out of range" },

            { ScsiKnownSenseCode.NotReady_LogicalUnitNotReady_CauseNotReportable, "Logical unit not ready, cause not reportable" },
            { ScsiKnownSenseCode.UnitAttention_PowerOnResetOrBusResetOccurred, "Power on, reset, or bus device reset occurred" },
            { ScsiKnownSenseCode.UnitAttention_ParametersChanged, "Parameters changed" },

            { ScsiKnownSenseCode.DataProtect_WriteProtected, "Write protected" },

            { ScsiKnownSenseCode.AbortedCommand_ScsiParityError, "SCSI parity error (command aborted)" },
        };

    /// <summary>
    /// Parse SCSI sense data from a byte array (fixed or descriptor format).
    /// </summary>
    /// <param name="buffer">Sense data buffer.</param>
    /// <param name="sense"></param>
    /// <param name="offset">Offset into the buffer where sense data starts.</param>
    public static bool TryParse(byte[] buffer, out ScsiSenseData sense, int offset = 0)
    {
        sense = null;

        // --- Top-level basic validation ---------------------------------------
        if (buffer == null)
        {
            return false;
        }

        if (offset < 0 || offset >= buffer.Length)
        {
            return false;
        }

        // Need at least 4 bytes to even know format
        if (buffer.Length - offset < 4)
        {
            return false;
        }

        // --- Read response code safely ----------------------------------------
        var b0 = buffer[offset];
        var responseCode = (byte)(b0 & 0x7F);

        var isDescriptorFormat = responseCode is 0x72 or 0x73;
        var isFixedFormat = responseCode is 0x70 or 0x71;

        if (!isDescriptorFormat && !isFixedFormat)
        {
            // Unknown/unsupported format
            return false;
        }

        ScsiSenseKey senseKey;
        byte asc = 0;
        byte ascq = 0;
        uint? information = null;

        // --- Descriptor format (0x72 or 0x73) ---------------------------------
        if (isDescriptorFormat)
        {
            // Descriptor format header is at least 4 bytes
            if (buffer.Length - offset < 4)
            {
                return false;
            }

            // Byte 1: Sense key (low nibble)
            senseKey = (ScsiSenseKey)(buffer[offset + 1] & 0x0F);

            // Byte 2: ASC
            asc = buffer[offset + 2];

            // Byte 3: ASCQ
            ascq = buffer[offset + 3];

            // INFORMATION descriptor parsing optional and skipped here
        }
        // --- Fixed format (0x70 or 0x71) --------------------------------------
        else
        {
            // Need at least 14 bytes for fixed‐format ASC/ASCQ + info
            if (buffer.Length - offset < 14)
            {
                return false;
            }

            // Byte 2: sense key (low nibble)
            senseKey = (ScsiSenseKey)(buffer[offset + 2] & 0x0F);

            // Bytes 3–6: INFORMATION (often LBA)
            unchecked
            {
                information =
                    (uint)((buffer[offset + 3] << 24) |
                           (buffer[offset + 4] << 16) |
                           (buffer[offset + 5] << 8) |
                            buffer[offset + 6]);
            }

            // Byte 12: ASC
            asc = buffer[offset + 12];

            // Byte 13: ASCQ
            ascq = buffer[offset + 13];
        }

        // --- Resolve KCQ into known enum --------------------------------------
        ScsiKnownSenseCode? knownCode = null;
        string description;

        if (KnownMap.TryGetValue((senseKey, asc, ascq), out var found))
        {
            knownCode = found;
            if (!KnownDescriptions.TryGetValue(found, out description))
            {
                description = found.ToString();
            }
        }
        else
        {
            // Fallback description
            description = GetDefaultDescription(senseKey, asc, ascq);
        }

        sense = new ScsiSenseData(
            responseCode: responseCode,
            isCurrent: responseCode is 0x70 or 0x72,
            isDescriptor: isDescriptorFormat,
            senseKey: senseKey,
            asc: asc,
            ascq: ascq,
            information: information,
            known: knownCode,
            description: description);

        return true;
    }

    /// <summary>
    /// Parse SCSI sense data from a byte array (fixed or descriptor format).
    /// </summary>
    /// <param name="buffer">Sense data buffer.</param>
    /// <param name="offset">Offset into the buffer where sense data starts.</param>
    public static ScsiSenseData Parse(byte[] buffer, int offset = 0)
    {
#if NET7_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(buffer);

        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offset, buffer.Length);

        ArgumentOutOfRangeException.ThrowIfLessThan(buffer.Length - offset, 4);
#else
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if (offset < 0 || offset >= buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (buffer.Length - offset < 4)
        {
            throw new ArgumentException("Sense buffer is too short.", nameof(buffer));
        }
#endif

        // Byte 0: Response code (low 7 bits) and VALID bit (bit 7 in fixed format).
        var raw0 = buffer[offset + 0];
        var responseCode = (byte)(raw0 & 0x7F);

        var isCurrent = responseCode is 0x70 or 0x72;
        var isDescriptorFormat = responseCode is 0x72 or 0x73;

        ScsiSenseKey senseKey;
        byte asc;
        byte ascq;
        uint? info = null;

        if (isDescriptorFormat)
        {
            // Descriptor format (0x72/0x73):
            // Byte 1: Sense key
            // Byte 2: ASC
            // Byte 3: ASCQ
            if (buffer.Length - offset < 4)
            {
                throw new ArgumentException("Descriptor sense buffer is too short.", nameof(buffer));
            }

            senseKey = (ScsiSenseKey)(buffer[offset + 1] & 0x0F);
            asc = buffer[offset + 2];
            ascq = buffer[offset + 3];

            // INFORMATION is in an Information Sense Data Descriptor (0x00) if present.
            // For simplicity we skip parsing descriptors here; you can add that if you need it.
        }
        else
        {
            // Fixed format (0x70/0x71):
            // Byte 2: Sense key (low 4 bits)
            // Bytes 3-6: INFORMATION (often LBA)
            // Byte 12: ASC
            // Byte 13: ASCQ
            if (buffer.Length - offset < 14)
            {
                throw new ArgumentException("Fixed-format sense buffer is too short.", nameof(buffer));
            }

            senseKey = (ScsiSenseKey)(buffer[offset + 2] & 0x0F);

            // INFORMATION (bytes 3..6) – big-endian
            info = (uint)(
                (buffer[offset + 3] << 24) |
                (buffer[offset + 4] << 16) |
                (buffer[offset + 5] << 8) |
                (buffer[offset + 6] << 0));

            asc = buffer[offset + 12];
            ascq = buffer[offset + 13];
        }

        // Try to resolve KCQ to a known code
        ScsiKnownSenseCode? knownCode = null;
        string description;

        if (KnownMap.TryGetValue((senseKey, asc, ascq), out var found))
        {
            knownCode = found;
            if (!KnownDescriptions.TryGetValue(found, out description))
            {
                description = found.ToString();
            }
        }
        else
        {
            // Default description if not known: based on SenseKey + ASC/ASCQ hex.
            description = GetDefaultDescription(senseKey, asc, ascq);
        }

        return new ScsiSenseData(
            responseCode: responseCode,
            isCurrent: isCurrent,
            isDescriptor: isDescriptorFormat,
            senseKey: senseKey,
            asc: asc,
            ascq: ascq,
            information: info,
            known: knownCode,
            description: description);
    }

    private static (ScsiSenseKey key, byte asc, byte ascq) ScsiTriple(ScsiKnownSenseCode code)
    {
        var value = (int)code;
        var asc = (byte)((value >> 8) & 0xFF);
        var ascq = (byte)(value & 0xFF);
        ScsiSenseKey key = (ScsiSenseKey)((value >> 16) & 0x0F);
        return (key, asc, ascq);
    }

    private static string GetDefaultDescription(ScsiSenseKey key, byte asc, byte ascq)
    {
        // Very simple fallback text – you can make this more elaborate if you like.
        return key switch
        {
            ScsiSenseKey.NoSense => "No specific sense information",
            ScsiSenseKey.RecoveredError => "Recovered error; operation succeeded with recovery",
            ScsiSenseKey.NotReady => "Logical unit not ready",
            ScsiSenseKey.MediumError => "Medium error (data error on the medium)",
            ScsiSenseKey.HardwareError => "Hardware error (internal failure)",
            ScsiSenseKey.IllegalRequest => "Illegal request (invalid CDB or parameters)",
            ScsiSenseKey.UnitAttention => "Unit attention (device or parameters changed)",
            ScsiSenseKey.DataProtect => "Data protect (write protection or similar)",
            ScsiSenseKey.BlankCheck => "Blank check (no recorded data)",
            ScsiSenseKey.CopyAborted => "Copy/compare operation aborted",
            ScsiSenseKey.AbortedCommand => "Command aborted by target",
            ScsiSenseKey.VolumeOverflow => "Volume overflow",
            ScsiSenseKey.Miscompare => "Miscompare during verify/compare",
            _ => "Unknown sense key"
        } + $" (ASC=0x{asc:X2}, ASCQ=0x{ascq:X2})";
    }
}
