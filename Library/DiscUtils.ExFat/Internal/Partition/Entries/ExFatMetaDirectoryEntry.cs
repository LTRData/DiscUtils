// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiscUtils.ExFat.Internal.IO;

namespace DiscUtils.ExFat.Internal.Partition.Entries;
/// <summary>
/// Groups entries by primary entry
/// </summary>
public class ExFatMetaDirectoryEntry : IDataProvider
{
    /// <summary>
    /// Gets all entries.
    /// </summary>
    /// <value>
    /// The entries.
    /// </value>
    public List<ExFatDirectoryEntry> Entries { get; } = new List<ExFatDirectoryEntry>();

    /// <summary>
    /// Gets the primary <see cref="ExFatDirectoryEntry"/> directory entry.
    /// </summary>
    /// <value>
    /// The primary.
    /// </value>
    public ExFatDirectoryEntry Primary => Entries.FirstOrDefault();

    /// <summary>
    /// Gets the secondary <see cref="ExFatDirectoryEntry"/> entries, if any.
    /// </summary>
    /// <value>
    /// The secondaries.
    /// </value>
    public IEnumerable<ExFatDirectoryEntry> Secondaries => Entries.Skip(1);

    /// <summary>
    /// Gets the <see cref="StreamExtensionExFatDirectoryEntry"/>, if any.
    /// </summary>
    /// <value>
    /// The secondary stream extension.
    /// </value>
    public StreamExtensionExFatDirectoryEntry SecondaryStreamExtension => Secondaries.OfType<StreamExtensionExFatDirectoryEntry>().FirstOrDefault();

    /// <summary>
    /// Gets the secondary <see cref="FileNameExtensionExFatDirectoryEntry"/>, if any.
    /// </summary>
    /// <value>
    /// The secondary file name extensions.
    /// </value>
    public IEnumerable<FileNameExtensionExFatDirectoryEntry> SecondaryFileNameExtensions => Secondaries.OfType<FileNameExtensionExFatDirectoryEntry>();

    /// <summary>
    /// Gets the extended file name, based on <see cref="SecondaryFileNameExtensions"/>.
    /// </summary>
    /// <value>
    /// The name of the extensions file.
    /// </value>
    public string ExtensionsFileName => string.Join("", SecondaryFileNameExtensions.Select(s => s.FileName.Value));

    /// <inheritdoc />
    /// <summary>
    /// Gets the data descriptor.
    /// </summary>
    /// <value>
    /// The data descriptor.
    /// </value>
    public DataDescriptor? DataDescriptor => Entries.OfType<IDataProvider>().FirstOrDefault()?.DataDescriptor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExFatMetaDirectoryEntry"/> class.
    /// </summary>
    /// <param name="entries">The entries.</param>
    public ExFatMetaDirectoryEntry(IEnumerable<ExFatDirectoryEntry> entries)
    {
        Entries.AddRange(entries);
    }

    /// <summary>
    /// Writes entries to stream.
    /// </summary>
    /// <param name="stream">The stream.</param>
    internal void Write(Stream stream)
    {
        Primary.Update(Secondaries.ToList());
        foreach (var entry in Entries)
        {
            entry.Write(stream);
        }
    }
}