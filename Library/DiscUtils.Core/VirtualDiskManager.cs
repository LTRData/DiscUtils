using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DiscUtils.Internal;

namespace DiscUtils;

/// <summary>
/// Helps discover and use VirtualDiskFactory's
/// </summary>
public static class VirtualDiskManager
{
    private static readonly object _registrationLock = new();

    static VirtualDiskManager()
    {
        ExtensionMap = new(StringComparer.OrdinalIgnoreCase);
        TypeMap = new(StringComparer.OrdinalIgnoreCase);
        DiskTransports = new(StringComparer.OrdinalIgnoreCase);
    }

    internal static ConcurrentDictionary<string, Func<VirtualDiskTransport>> DiskTransports { get; }
    internal static ConcurrentDictionary<string, VirtualDiskFactory> ExtensionMap { get; }

    /// <summary>
    /// Gets the set of disk formats supported as an array of file extensions.
    /// </summary>
    public static ICollection<string> SupportedDiskFormats => ExtensionMap.Keys;

    /// <summary>
    /// Gets the set of disk types supported, as an array of identifiers.
    /// </summary>
    public static ICollection<string> SupportedDiskTypes => TypeMap.Keys;

    internal static ConcurrentDictionary<string, VirtualDiskFactory> TypeMap { get; }

    /// <summary>Registers a disk factory without inspecting attributes or assemblies.</summary>
    /// <param name="type">The disk type identifier, for example VHD.</param>
    /// <param name="fileExtensions">Extensions, with or without a leading period.</param>
    /// <param name="factory">The factory shared by the type and extension mappings.</param>
    /// <remarks>Keys are case insensitive. Duplicate type or extension keys throw
    /// <see cref="ArgumentException"/> without changing any mappings.</remarks>
    public static void RegisterVirtualDiskFactory(string type, string[] fileExtensions, VirtualDiskFactory factory)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));
        if (fileExtensions == null) throw new ArgumentNullException(nameof(fileExtensions));
        if (factory == null) throw new ArgumentNullException(nameof(factory));

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in fileExtensions)
        {
            if (extension == null) throw new ArgumentException("An extension cannot be null.", nameof(fileExtensions));
            if (!extensions.Add(extension.Replace(".", string.Empty)))
            {
                throw new ArgumentException("Duplicate file extension.", nameof(fileExtensions));
            }
        }

        lock (_registrationLock)
        {
            if (TypeMap.ContainsKey(type)) throw new ArgumentException("Disk type is already registered.", nameof(type));
            foreach (var extension in extensions)
            {
                if (ExtensionMap.ContainsKey(extension))
                {
                    throw new ArgumentException("File extension is already registered.", nameof(fileExtensions));
                }
            }

            TypeMap.TryAdd(type, factory);
            foreach (var extension in extensions) ExtensionMap.TryAdd(extension, factory);
        }
    }

    /// <summary>Registers a factory that creates a fresh transport for each disk operation.</summary>
    /// <param name="scheme">The URI scheme (case insensitive).</param>
    /// <param name="factory">The transport constructor delegate.</param>
    /// <remarks>Supports third-party URI schemes without assembly scanning.
    /// Duplicate schemes throw <see cref="ArgumentException"/>.</remarks>
    public static void RegisterVirtualDiskTransport(string scheme, Func<VirtualDiskTransport> factory)
    {
        if (scheme == null) throw new ArgumentNullException(nameof(scheme));
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        if (!DiskTransports.TryAdd(scheme, factory))
        {
            throw new ArgumentException("Transport scheme is already registered.", nameof(scheme));
        }
    }

    /// <summary>
    /// Locates VirtualDiskFactory factories attributed with VirtualDiskFactoryAttribute, and types marked with VirtualDiskTransportAttribute, that are able to work with Virtual Disk types.
    /// </summary>
    /// <param name="assembly">An assembly to scan</param>
#if NET5_0_OR_GREATER
    [RequiresUnreferencedCode("Assembly discovery requires untrimmed factory types and constructors. Use explicit registration instead.")]
#endif
    public static void RegisterVirtualDiskTypes(Assembly assembly)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));
        if (Setup.SetupHelper.IsAssemblyRegistered(assembly)) return;

        foreach (var type in assembly.GetTypes())
        {
            var diskFactoryAttribute = type.GetCustomAttribute<VirtualDiskFactoryAttribute>(false);
            if (diskFactoryAttribute != null)
            {
                var factory = (VirtualDiskFactory)Activator.CreateInstance(type)!;
                RegisterVirtualDiskFactory(diskFactoryAttribute.Type, diskFactoryAttribute.FileExtensions, factory);
            }

            var diskTransportAttribute = type.GetCustomAttribute<VirtualDiskTransportAttribute>(false);
            if (diskTransportAttribute != null)
            {
                RegisterVirtualDiskTransport(diskTransportAttribute.Scheme,
                    () => (VirtualDiskTransport)Activator.CreateInstance(type)!);
            }
        }
    }
}
