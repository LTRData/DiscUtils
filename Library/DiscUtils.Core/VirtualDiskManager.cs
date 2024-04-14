using System;
using System.Collections.Generic;
using System.Reflection;
using DiscUtils.Internal;

namespace DiscUtils;

/// <summary>
/// Helps discover and use VirtualDiskFactory's
/// </summary>
public static class VirtualDiskManager
{
    static VirtualDiskManager()
    {
        ExtensionMap = new Dictionary<string, VirtualDiskFactory>(StringComparer.OrdinalIgnoreCase);
        TypeMap = new Dictionary<string, VirtualDiskFactory>(StringComparer.OrdinalIgnoreCase);
        DiskTransports = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
    }

    internal static Dictionary<string, Type> DiskTransports { get; }
    internal static Dictionary<string, VirtualDiskFactory> ExtensionMap { get; }

    /// <summary>
    /// Gets the set of disk formats supported as an array of file extensions.
    /// </summary>
    public static ICollection<string> SupportedDiskFormats => ExtensionMap.Keys;

    /// <summary>
    /// Gets the set of disk types supported, as an array of identifiers.
    /// </summary>
    public static ICollection<string> SupportedDiskTypes => TypeMap.Keys;

    internal static Dictionary<string, VirtualDiskFactory> TypeMap { get; }

    /// <summary>
    /// Locates VirtualDiskFactory factories attributed with VirtualDiskFactoryAttribute, and types marked with VirtualDiskTransportAttribute, that are able to work with Virtual Disk types.
    /// </summary>
    /// <param name="assembly">An assembly to scan</param>
    public static void RegisterVirtualDiskTypes(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            var diskFactoryAttribute = type.GetCustomAttribute<VirtualDiskFactoryAttribute>(false);
            if (diskFactoryAttribute != null)
            {
                var factory = (VirtualDiskFactory)Activator.CreateInstance(type);
                TypeMap.Add(diskFactoryAttribute.Type, factory);

                foreach (var extension in diskFactoryAttribute.FileExtensions)
                {
                    ExtensionMap.Add(extension, factory);
                }
            }

            var diskTransportAttribute = type.GetCustomAttribute<VirtualDiskTransportAttribute>(false);
            if (diskTransportAttribute != null)
            {
                DiskTransports.Add(diskTransportAttribute.Scheme, type);
            }
        }
    }
}