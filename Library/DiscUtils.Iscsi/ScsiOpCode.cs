using System;
using System.Collections.Generic;
using System.Text;

namespace DiscUtils.Iscsi;

#pragma warning disable CA1069 // Enums values should not be duplicated

public enum ScsiOpServiceAction : byte
{
    // Common service actions for 0x9E
    ReadCapacity16 = 0x10,   // SA=0x10 for opcode 0x9E
    GetLBAStatus = 0x12,
    Read16 = 0x88,   // effectively READ(16)
    Write16 = 0x8A,   // effectively WRITE(16)
}

/// <summary>
/// SCSI Command Descriptor Block (CDB) opcodes from SPC, SBC, SSC, MMC.
/// Only the commonly used standard commands are included.
/// </summary>
public enum ScsiOpCode : byte
{
    // -------------------------
    // 6-byte CDB commands
    // -------------------------
    TestUnitReady = 0x00,
    RezeroUnit = 0x01,   // legacy
    RequestSense = 0x03,
    FormatUnit = 0x04,
    Read6 = 0x08,
    Write6 = 0x0A,
    Seek6 = 0x0B,
    Inquiry = 0x12,
    ModeSelect6 = 0x15,
    ModeSense6 = 0x1A,
    StartStopUnit = 0x1B,
    ReceiveDiagnosticResults = 0x1C,
    SendDiagnostic = 0x1D,
    PreventAllowMediumRemoval = 0x1E,

    // -------------------------
    // 10-byte CDB commands
    // -------------------------
    ReadCapacity10 = 0x25,
    Read10 = 0x28,
    Write10 = 0x2A,
    Seek10 = 0x2B,
    WriteVerify10 = 0x2E,
    Verify10 = 0x2F,
    SynchronizeCache10 = 0x35,
    WriteBuffer = 0x3B,
    ReadBuffer = 0x3C,
    ReadLong10 = 0x3E,
    WriteLong10 = 0x3F,

    // -------------------------
    // Service Action opcodes (0x9E, 0x9F)
    // -------------------------
    ServiceActionIn = 0x9E,
    ServiceActionOut = 0x9F,

    // -------------------------
    // 12-byte CDB commands
    // -------------------------
    Read12 = 0xA8,
    Write12 = 0xAA,
    WriteVerify12 = 0xAE,
    Verify12 = 0xAF,

    // -------------------------
    // 16-byte CDB commands
    // -------------------------
    Read16 = 0x88,
    Write16 = 0x8A,
    Verify16 = 0x8F,
    WriteVerify16 = 0x8E,
    SynchronizeCache16 = 0x91,
    WriteAtomic16 = 0x9C,

    // -------------------------
    // SPC (control / management)
    // -------------------------
    ReportLuns = 0xA0,
    ReportSupportedOpCodes = 0xA3,
    ReportSupportedTaskManagement = 0xA4,
    ReportIdentifiers = 0xA1,
    PersistentReserveIn = 0x5E,
    PersistentReserveOut = 0x5F,
    ReadAttribute = 0x8C,
    WriteAttribute = 0x8D,
    ModeSelect10 = 0x55,
    ModeSense10 = 0x5A,

    // -------------------------
    // Block Commands (SBC)
    // -------------------------
    ReadDefectData10 = 0x37,
    ReadDefectData12 = 0xB7,
    CompareAndWrite = 0x89,
    OrWrite = 0x8B,
    WriteSame10 = 0x41,
    WriteSame16 = 0x93,
    WriteStream16 = 0x9B,
    ReadStream16 = 0x9A,

    // -------------------------
    // Synchronization / cache
    // -------------------------
    SynchronizeCache = 0x35,

    // -------------------------
    // MMC (optical media)
    // -------------------------
    ReadTocPmaAtip = 0x43,
    ReadDiscInformation = 0x51,
    ReadTrackInformation = 0x52,
    ReserveTrack = 0x53,
    SendOpcInformation = 0x54,

    // -------------------------
    // SSC (tape devices)
    // -------------------------
    Locate = 0x2B,
    ReadPosition = 0x34,
    LoadUnload = 0x1B,
    Space = 0x11,
    WriteFilemarks = 0x10
}
