namespace DiscUtils.ReFs.Mount.Enums;

/// <summary>
///     The flags DWORD of the checkpoint record (u32 @0x78 of the CHKP cluster). Windows changes them in memory, so they reach the disk with the next
///     checkpoint. Bit meanings are inferred from observed behaviour; names are ours where no official name is known. Bit 0x1 and the bits from 0x1000
///     up are not used on Windows Server 2025 media before its servicing updates; later Windows Server 2025 builds add 0x2000 and Insider build 29708
///     adds 0x4000. Another structure has a flags DWORD at the same offset with different bits: not these.
/// </summary>
[Flags]
internal enum CheckpointFeatures : uint
{
    None = 0,

    /// <summary>
    ///     The volume completed a read-write start. Never
    ///     cleared. The name is inferred.
    /// </summary>
    StartedReadWrite = 0x2,

    /// <summary>Every data write is redirected to new clusters.</summary>
    ReallocateAllDataWrites = 0x4,

    /// <summary>The stream-table delete queue has been used. Never cleared; the name is inferred.</summary>
    StreamDeleteQueueUsed = 0x8,

    /// <summary>Data deduplication is enabled.</summary>
    DedupEnabled = 0x10,

    /// <summary>Weak references (block-clone reference counts) are enabled.</summary>
    WeakReferencesEnabled = 0x20,

    /// <summary>
    ///     The volume is marked as a developer volume. Whether Windows
    ///     honours the mark and treats the volume as "trusted" is decided by registry settings of the machine, not by the disk.
    /// </summary>
    DeveloperVolume = 0x40,

    /// <summary>
    ///     Only tested: when set, or when the stream checksum kind is CRC32C, new checksummed runs get run flag 0x80, otherwise 0x100.
    ///     No setter was observed.
    /// </summary>
    StreamChecksumRunFlag = 0x80,

    /// <summary>Dirty-state tracking is enabled.</summary>
    DirtyStateTracking = 0x100,

    /// <summary>Extended record layout: the record carries the root offset table description at +0x94/+0x98.</summary>
    ExtendedRecordLayout = 0x200,

    /// <summary>A rollback-protection blob follows (format version 3.13 and later): offset @0x9C, size @0xA0.</summary>
    RollbackProtection = 0x400,

    /// <summary>
    ///     The weak-reference state is inconsistent. No
    ///     setter was observed; the name is inferred.
    /// </summary>
    WeakReferencesInconsistent = 0x800,

    /// <summary>
    ///     An HMAC area for TPM volume attestation follows (newer Windows Server 2025 builds, format version 3.13 and later): offset @0xA4, size @0xA8
    ///     (72 bytes, or 72 plus a power of two), after the rollback-protection blob. The area is written whether or not attestation is on; only the
    ///     digest needs it. Set on every volume the lab's updated Windows Server 2025 writes. Layout: see <see cref="Checkpoint" />.
    /// </summary>
    AttestationHmac = 0x2000,

    /// <summary>
    ///     Windows mounts the volume without redo logging (Insider build 29708 only; the name is inferred). Not seen on an image; what a mount without
    ///     logging leaves in the log area is not known.
    /// </summary>
    RedoLoggingDisabledOnMount = 0x4000
}