using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace DiscUtils.Setup;

/// <summary>
/// Helps setup new DiscUtils dependencies, when loaded into target programs
/// </summary>
public static class SetupHelper
{
    private static readonly HashSet<string> _alreadyLoaded;
    private static readonly HashSet<string> _registering;

    static SetupHelper()
    {
        _alreadyLoaded = [];
        _registering = [];
    }

    /// <summary>
    /// Registers the types provided by an assembly to all relevant DiscUtils managers
    /// </summary>
    /// <param name="assembly"></param>
#if NET5_0_OR_GREATER
    [RequiresUnreferencedCode("Assembly discovery requires untrimmed factory types and constructors. Use explicit registration instead.")]
#endif
    public static void RegisterAssembly(Assembly assembly)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));
        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        RegisterAssembly(assembly, () =>
        {
            FileSystemManager.RegisterFileSystems(assembly);
            VirtualDiskManager.RegisterVirtualDiskTypes(assembly);
            VolumeManager.RegisterLogicalVolumeFactory(assembly);
            Partitions.PartitionTable.RegisterPartitionTableFactories(assembly);
        });
    }

    /// <summary>Runs an assembly's explicit registrations once, without scanning its types.</summary>
    /// <param name="assembly">The assembly providing the implementations.</param>
    /// <param name="register">Calls to the ordinary factory registration APIs.</param>
    /// <remarks>Shares the registration guard with reflection-based assembly registration.</remarks>
    public static void RegisterAssembly(Assembly assembly, Action register)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));
        if (register == null) throw new ArgumentNullException(nameof(register));
        lock (_alreadyLoaded)
        {
            var name = assembly.FullName ?? assembly.GetName().FullName;
            if (!_alreadyLoaded.Add(name))
            {
                return;
            }

            // Preserve the legacy guard against a factory constructor registering
            // its own assembly again, while allowing the scanners in this callback.
            _registering.Add(name);
            try
            {
                register();
            }
            finally
            {
                _registering.Remove(name);
            }
        }
    }

    internal static bool IsAssemblyRegistered(Assembly assembly)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));
        lock (_alreadyLoaded)
        {
            var name = assembly.FullName ?? assembly.GetName().FullName;
            return _alreadyLoaded.Contains(name) && !_registering.Contains(name);
        }
    }

    /// <summary>
    /// Allows intercepting any file open operation
    /// </summary>
    /// <remarks>
    /// Can be used to wrap the opened file for special use cases,
    /// modify the parameters for opening files, validate file names 
    /// and many more.
    /// </remarks>
    public static event EventHandler<FileOpenEventArgs>? OpeningFile;

    internal static void OnOpeningFile(object sender, FileOpenEventArgs e)
    {
        OpeningFile?.Invoke(sender, e);
    }
}
