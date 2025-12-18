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
    private static Dictionary<string, VirtualDiskFactory>? _extensionMap;
    private static Dictionary<string, VirtualDiskFactory>? _typeMap;
    private static Dictionary<string, Func<VirtualDiskTransport>>? _diskTransports;

    internal static Dictionary<string, Func<VirtualDiskTransport>> DiskTransports
    {
        get
        {
            if (_diskTransports == null)
            {
                InitializeMaps();
            }
            return _diskTransports!;
        }
    }

    internal static Dictionary<string, VirtualDiskFactory> ExtensionMap
    {
        get
        {
            if (_extensionMap == null)
            {
                InitializeMaps();
            }
            return _extensionMap!;
        }
    }

    /// <summary>
    /// Gets the set of disk formats supported as an array of file extensions.
    /// </summary>
    public static ICollection<string> SupportedDiskFormats => ExtensionMap.Keys;

    /// <summary>
    /// Gets the set of disk types supported, as an array of identifiers.
    /// </summary>
    public static ICollection<string> SupportedDiskTypes => TypeMap.Keys;

    internal static Dictionary<string, VirtualDiskFactory> TypeMap
    {
        get
        {
            if (_typeMap == null)
            {
                InitializeMaps();
            }
            return _typeMap!;
        }
    }

    private static void InitializeMaps()
    {
        if (_typeMap == null)
        {
            _extensionMap = new Dictionary<string, VirtualDiskFactory>(StringComparer.OrdinalIgnoreCase);
            _typeMap = new Dictionary<string, VirtualDiskFactory>(StringComparer.OrdinalIgnoreCase);
            _diskTransports = new Dictionary<string, Func<VirtualDiskTransport>>(StringComparer.OrdinalIgnoreCase);

            // Auto-scan Core assembly if not initialized
            RegisterVirtualDiskTypes(typeof(VirtualDiskManager).Assembly);
        }
    }

    private static void EnsureInitialized()
    {
        if (_typeMap == null)
        {
            _extensionMap = new Dictionary<string, VirtualDiskFactory>(StringComparer.OrdinalIgnoreCase);
            _typeMap = new Dictionary<string, VirtualDiskFactory>(StringComparer.OrdinalIgnoreCase);
            _diskTransports = new Dictionary<string, Func<VirtualDiskTransport>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Registers a VirtualDiskFactory instance.
    /// </summary>
    /// <param name="factory">The factory to register.</param>
    public static void RegisterVirtualDiskFactory(VirtualDiskFactory factory)
    {
        EnsureInitialized();

        var type = factory.GetType();
        var diskFactoryAttribute = type.GetCustomAttribute<VirtualDiskFactoryAttribute>(false);
        if (diskFactoryAttribute != null)
        {
            if (!TypeMap.ContainsKey(diskFactoryAttribute.Type))
            {
                TypeMap.Add(diskFactoryAttribute.Type, factory);
            }

            foreach (var extension in diskFactoryAttribute.FileExtensions)
            {
                if (!ExtensionMap.ContainsKey(extension))
                {
                    ExtensionMap.Add(extension, factory);
                }
            }
        }
    }

    /// <summary>
    /// Registers a VirtualDiskTransport type.
    /// </summary>
    /// <param name="scheme">The URI scheme.</param>
    /// <param name="type">The type implementing VirtualDiskTransport.</param>
    public static void RegisterVirtualDiskTransport(string scheme, Type type)
    {
        EnsureInitialized();
        if (!DiskTransports.ContainsKey(scheme))
        {
            DiskTransports.Add(scheme, () => (VirtualDiskTransport)Activator.CreateInstance(type, true)!);
        }
    }

    /// <summary>
    /// Registers a VirtualDiskTransport factory.
    /// </summary>
    /// <param name="scheme">The URI scheme.</param>
    /// <param name="factory">The factory method.</param>
    internal static void RegisterVirtualDiskTransport(string scheme, Func<VirtualDiskTransport> factory)
    {
        EnsureInitialized();
        if (!DiskTransports.ContainsKey(scheme))
        {
            DiskTransports.Add(scheme, factory);
        }
    }

    /// <summary>
    /// Locates VirtualDiskFactory factories attributed with VirtualDiskFactoryAttribute, and types marked with VirtualDiskTransportAttribute,
    /// that are able to work with Virtual Disk types.
    /// </summary>
    /// <param name="assembly">An assembly to scan</param>
    public static void RegisterVirtualDiskTypes(Assembly assembly)
    {
        Console.WriteLine($"VirtualDiskManager: Scanning assembly {assembly.FullName} for VirtualDiskTypes");
        EnsureInitialized();
        foreach (var type in assembly.GetTypes())
        {
            var diskFactoryAttribute = type.GetCustomAttribute<VirtualDiskFactoryAttribute>(false);
            if (diskFactoryAttribute != null)
            {
                Console.WriteLine($"VirtualDiskManager: Found VirtualDiskFactory: {type.FullName}");
                try
                {
                    var factory = (VirtualDiskFactory)Activator.CreateInstance(type, true)!;
                    TypeMap.Add(diskFactoryAttribute.Type, factory);

                    foreach (var extension in diskFactoryAttribute.FileExtensions)
                    {
                        ExtensionMap.Add(extension, factory);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"VirtualDiskManager: Error instantiating {type.FullName}: {ex}");
                    throw;
                }
            }

            var diskTransportAttribute = type.GetCustomAttribute<VirtualDiskTransportAttribute>(false);
            if (diskTransportAttribute != null)
            {
                Console.WriteLine($"VirtualDiskManager: Found VirtualDiskTransport: {type.FullName} for scheme {diskTransportAttribute.Scheme}");
                DiskTransports.Add(diskTransportAttribute.Scheme, () => 
                {
                    try
                    {
                        return (VirtualDiskTransport)Activator.CreateInstance(type, true)!;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"VirtualDiskManager: Error instantiating transport {type.FullName}: {ex}");
                        throw;
                    }
                });
            }
        }
    }
}