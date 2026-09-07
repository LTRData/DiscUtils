# Format registration and Native AOT

Built-in discovery uses the existing disk, file-system, transport, partition-table
and logical-volume registries. An incremental source generator reads the existing
five discovery attributes while each DiscUtils library is compiled. It emits
concrete constructor calls and, for transports, constructor delegates. Runtime
lookups and third-party registrations use the same registries.

`DiskImageBuilder.GetBuilder` now uses `VirtualDiskManager`'s type map as well.
Previously it scanned Core alone and therefore missed builders in format assemblies.

## Automatic initialization

Each participating assembly exposes a small `Formats.Register()` entry point, for
example `DiscUtils.Vhd.Formats.Register()`. It calls dependency registrations and
registers its own factories once. A module initializer calls this entry point.
The existing complete/container/file-system/transport setup helpers forward to
these entry points and remain valid.

A package reference alone does not necessarily cause the CLR to execute a library's
module initializer. To cover applications that use only Core APIs, Core also ships
the generator as a **build-time** tool through `buildTransitive`. In a C# consumer,
it emits a module initializer with direct calls to the referenced DiscUtils
`Formats.Register()` methods. These calls also retain the required code when trimmed.
There is no assembly scan, `Activator`, linker root list, or runtime Roslyn dependency
on this path. Format metadata is not copied into package assets or maintained by hand.

Automatic consumer initialization requires a Roslyn 4.8-or-later compiler and
C# 9-or-later language mode. All existing library target frameworks remain supported;
older TFMs use a compiler marker for module initialization. The repository's existing
PolySharp dependency supplies that marker when present. No compatibility package is
added to consuming applications.

Older C# language modes receive warning `DUAOT002` and keep using their existing
setup calls. They are not required to change language version to keep building.

For non-C# consumers, direct DLL references, or builds that exclude transitive build
assets, call the relevant `Formats.Register()` method or existing meta-package setup
helper. Source consumers using project references outside this repository should
likewise reference the generator as an analyzer or call the setup entry point.
Merely loading a DLL without using any of its code is not an initialization guarantee.

## Third-party formats

External libraries do not need the generator. Register ordinary factory instances:

```csharp
VirtualDiskManager.RegisterVirtualDiskFactory(
    "MYDISK", new[] { ".mydisk" }, new MyDiskFactory());
FileSystemManager.RegisterFileSystems(new MyFileSystemFactory());
VirtualDiskManager.RegisterVirtualDiskTransport("mytransport", () => new MyTransport());
PartitionTable.RegisterPartitionTableFactory(new MyPartitionTableFactory());
VolumeManager.RegisterLogicalVolumeFactory(new MyLogicalVolumeFactory());
```

The transport, partition-table and logical-volume base classes and discovery
attributes are now public, alongside the existing disk/file-system base classes.
`LogicalVolumeInfo`'s constructor is public so external factories can return volumes.
Registries remain extensible after automatic initialization.

To guard a complete set of registrations against repeated setup calls, use:

```csharp
DiscUtils.Setup.SetupHelper.RegisterAssembly(typeof(MyDiskFactory).Assembly, () =>
{
    // Register all providers from this assembly that the application intends to use.
    VirtualDiskManager.RegisterVirtualDiskFactory(
        "MYDISK", new[] { ".mydisk" }, new MyDiskFactory());
});
```

This shares the existing assembly identity guard with reflection registration.
Callbacks are not transactions: a failing callback can leave earlier registrations
in place. As with legacy assembly setup, a failed attempt remains marked as
registered. Reentrant assembly setup from a factory constructor is ignored.
Individual disk-factory registration validates all keys before changing any mappings.

For JIT applications, `SetupHelper.RegisterAssembly(assembly)` and the manager
overloads accepting an `Assembly` still discover attributed plugins using reflection.
They run the module initializer first, so a built-in library already registered by
generated code is not registered twice. Reflection entry points are annotated with
`RequiresUnreferencedCode` on .NET 5+; explicit registration is the supported AOT path.

## Duplicates and ordering

* Disk type names, extensions and transport schemes remain case insensitive.
  Extension normalization matches the existing attribute (periods are removed).
  Duplicate keys throw `ArgumentException`; existing registrations are not replaced.
  Disk-factory conflicts now leave all mappings unchanged, rather than potentially
  adding a type and some extensions before throwing.
* Explicit file-system and partition-table registrations append, including repeated
  instances. Detection follows that order. Logical-volume factories retain their
  existing `ConcurrentBag` semantics and unspecified enumeration order.
* Assembly-level registration runs once, including a mix of generated and reflection
  setup. Low-level reflection registration of an unguarded external assembly retains
  its existing duplicate behavior.
* Generated factories are ordered by fully qualified type name within each assembly;
  dependency setup calls are ordered by fully qualified entry-point name. The former
  reflection order came from `Assembly.GetTypes()` and was not a specified precedence
  contract. Applications with overlapping custom detectors should register them
  explicitly in their intended order. Built-ins now initialize before application
  code, so plugins cannot claim an existing built-in disk key before setup.
* Disk dictionaries are concurrent to permit registration while lookups occur.
  Enumeration of supported names/extensions is a snapshot with no ordering contract.

## Validation

```sh
dotnet build DiscUtils.slnx
dotnet test Tests/RegistrationTests/RegistrationTests.csproj -f net10.0
dotnet test Tests/SourceGeneratorTests/SourceGeneratorTests.csproj
dotnet test Tests/LibraryTests/LibraryTests.csproj -f net10.0
dotnet publish Tests/NativeAotSmoke/NativeAotSmoke.csproj -c Release -r linux-x64
```

Run the published `NativeAotSmoke` executable. It exercises automatic VHD type and
extension lookup, VHD create/open, image-builder lookup, FAT detection/open, partition
and logical-volume discovery, and a third-party transport. Its source references only
Core/Streams types and does not call any initialization or assembly-scanning API.

To test package delivery, pack Streams, Core, Vhd, Fat and Lvm into a local feed,
copy the two smoke-project source files outside the repository, and publish with
`-p:UsePackageReferences=true -p:DiscUtilsPackageVersion=<packed-version>` using that
feed. This must work without this repository's `Directory.Build.targets`.

This work does not establish that every DiscUtils operation is AOT-compatible.
In particular, iSCSI protocol-key reflection and any trimming/native interop issues
in format implementations or dependencies are outside the discovery change.
The PR records the actual host, commands, outcomes and remaining validation limits.
