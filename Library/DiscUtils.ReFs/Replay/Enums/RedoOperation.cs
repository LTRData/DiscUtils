namespace DiscUtils.ReFs.Replay.Enums;

/// <summary>
///     Redo operation codes. Codes 0-4 are confirmed on a real log; the others are
///     named from the replay behaviour they appear to select and are provisional.
/// </summary>
internal enum RedoOperation : uint
{
    CreateTable = 0,
    InsertRow = 1,
    DeleteRow = 2,
    UpdateRow = 3,
    UpdateDataWithRoot = 4,
    ReparentTable = 5,
    StreamAllocate = 6,
    StreamFree = 7,
    SetRangeValid = 8,
    SetRangeInvalid = 9,
    DuplicateExtents = 10,
    ModifyStreamExtent = 11,
    StripChecksums = 12,
    SetIntegrityInformation = 13,
    SetParentId = 14,
    DeleteTable = 15,
    SetObjectRecordPayload = 16,
    AddSchema = 17,
    TreeUpdateMarker = 18,
    AddContainer = 19,
    MoveContainer = 20,
    LogAdvanceMarker = 21,
    ContainerCompaction = 24,
    DeleteCompressionUnitOffsets = 25,
    GhostExtents = 27,
    CompactionUnreserve = 28,

    /// <summary>
    ///     Clears a table's parent link. Logged when a directory is deleted; no row changes.
    /// </summary>
    UnlinkParentObjectId = 29,
    PrepareEntryForMerge = 30,
    UpdateStreamSummary = 31,
    UpdateStreamUserPayload = 32,
    StreamPersistFastRunInsertion = 33,
    TableSetSummaryUpdate = 34,
    TableSetShadowTreeUpdate = 35,
    TableSetCommitMerge = 36,
    TableSetStrongRefMerge = 38,
    SetDefaultCompressionParameters = 39,
    BreakWeakReferences = 40,
    DuplicateCluster = 41,
    ChangeRangeEncryptedState = 42,

    /// <summary>
    ///     Logged by Windows build 29683 where earlier builds log <see cref="DeleteTable" /> (lab image <c>w2028pre-dirtynames</c>); replayed like
    ///     <see cref="DeleteTable" />. On a name-row path it also removes the row: the 29683 log has no separate DeleteRow for the name, unlike 26100's.
    ///     Derived from images, checked against Windows' read-back.
    /// </summary>
    DeleteTableAndRow = 45
}