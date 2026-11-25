using System.Globalization;
using DiscUtils.DeduplicationRole.ChunkStore.Enums;

namespace DiscUtils.DeduplicationRole.ChunkStore;

/// <summary>A chunk store container: its kind, id and generation, and its file name <c>{id:x8}.{generation:x8}.ccc</c>.</summary>
internal readonly record struct DedupContainerName(DedupContainerKind Kind, uint Id, uint Generation)
{
    private const string Extension = "ccc";

    public string FileName => $"{Id:x8}.{Generation:x8}.{Extension}";

    /// <summary>The container a file name names; false when the name is not a container's.</summary>
    public static bool TryParse(DedupContainerKind kind, string fileName, out DedupContainerName name)
    {
        name = default;
        string[] parts = fileName.Split('.');
        if (parts.Length != 3 || !parts[2].Equals(Extension, StringComparison.OrdinalIgnoreCase)
                              || !uint.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint id)
                              || !uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint generation))
            return false;

        name = new DedupContainerName(kind, id, generation);
        return true;
    }
}
