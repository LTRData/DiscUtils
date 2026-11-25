using DiscUtils.DeduplicationRole.Reparse.Enums;

namespace DiscUtils.DeduplicationRole.Reparse;

/// <summary>One entry of a property block's table: the value's type, size and offset from the block's signature.</summary>
internal readonly record struct DedupProperty(DedupPropertyType Type, int Size, int Offset);
