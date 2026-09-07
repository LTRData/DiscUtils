# Format registration and Native AOT

Built-in discovery uses the existing disk, file-system, transport, partition-table
and logical-volume registries. An incremental source generator reads the existing
five discovery attributes while each DiscUtils library is compiled. It emits
concrete constructor calls and, for transports, constructor delegates. Runtime
lookups and third-party registrations use the same registries.

`DiskImageBuilder.GetBuilder` shares `VirtualDiskManager`'s type map. Previously it
scanned Core alone and therefore missed builders in format assemblies.

## Explicit initialization

Call the registration entry point for the formats your application uses:

```csharp
DiscUtils.Vhd.Formats.Register();
DiscUtils.Fat.Formats.Register();
```

Each entry point registers its DiscUtils dependencies first, then its own factories.
For example, VHD registration includes Core's raw disk and file transport providers.
The existing setup helpers also use generated direct calls:

```csharp
DiscUtils.Complete.SetupHelper.SetupComplete();
DiscUtils.Containers.SetupHelper.SetupContainers();
DiscUtils.FileSystems.SetupHelper.SetupFileSystems();
DiscUtils.Transports.SetupHelper.SetupTransports();
DiscUtils.ExFat.ExFatSetupHelper.SetupFileSystems();
```

Choose the individual entries or helper appropriate to your application. These calls
are safe under trimming/Native AOT and can be repeated. Their shared assembly guard
also prevents duplicate registration when mixed with reflection-based assembly setup.

There are no generated module initializers. Loading or referencing a format assembly
does not register its formats. Core does not ship an analyzer, `buildTransitive`
registration target, or runtime generator dependency. Applications do not run the
DiscUtils generator and have no registration-related Roslyn or C# 9 requirement.
The same explicit calls work with package, project and direct DLL references.

Core's existing lazy defaults are retained: partition/volume discovery ensures Core's
generated registrations are present, and reflection-based assembly setup registers
Core first. This preserves direct Core API behavior without scanning Core or activating
any additional format packages. Other formats require explicit setup.

## Third-party formats

External libraries can register ordinary factory instances without the generator:

```csharp
VirtualDiskManager.RegisterVirtualDiskFactory(
    "MYDISK", new[] { ".mydisk" }, new MyDiskFactory());
FileSystemManager.RegisterFileSystems(new MyFileSystemFactory());
VirtualDiskManager.RegisterVirtualDiskTransport("mytransport", () => new MyTransport());
VolumeManager.RegisterLogicalVolumeFactory(new MyLogicalVolumeFactory());
```

Transport and logical-volume factories participate in the existing extensible
assembly registries, so their base classes and discovery attributes are public.
`LogicalVolumeInfo`'s constructor is public so external volume factories can return
mapped volumes. Partition-table factories and their registration methods remain
internal: their existing discovery is confined to Core.

A handwritten library entry point can use the assembly identity guard:

```csharp
public static void Register()
{
    DiscUtils.Core.Formats.Register();
    DiscUtils.Setup.SetupHelper.RegisterAssembly(typeof(MyDiskFactory).Assembly, () =>
    {
        VirtualDiskManager.RegisterVirtualDiskFactory(
            "MYDISK", new[] { ".mydisk" }, new MyDiskFactory());
    });
}
```

For JIT applications, `DiscUtils.Setup.SetupHelper.RegisterAssembly(assembly)` and the
existing manager overloads accepting an `Assembly` still discover attributed plugins
using reflection. They call the same registry APIs. Reflection entry points are
annotated with `RequiresUnreferencedCode` on .NET 5+; explicit registration is the
supported AOT path.

## Optional generation in a private library

The generator is opt-in build infrastructure for a format library. For a source
checkout, add this to the private library's project (adjust the analyzer path):

```xml
<PropertyGroup>
  <DiscUtilsGenerateRegistration>true</DiscUtilsGenerateRegistration>
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="path/to/DiscUtils.SourceGenerator.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false"
                    PrivateAssets="all"
                    GlobalPropertiesToRemove="TargetFramework;TargetFrameworks"
                    SkipGetTargetFrameworkProperties="true" />
  <CompilerVisibleProperty Include="DiscUtilsGenerateRegistration" />
</ItemGroup>
```

Reference Core as usual and attribute the library's implementations with the existing
discovery attributes. For an assembly named `MyLibrary`, the generator emits public
`MyLibrary.Formats.Register()`. Assembly names used for generated namespaces must be
valid C# namespace names. The application explicitly calls that method during startup.
Generation requires a Roslyn 4.8+ compiler in the format library build only; its output
also compiles in C# 7.3. The consuming application needs only the compiled library.
There is no separately published generator package in this change.

The generated method calls the common assembly registration guard directly. It adds
no static initialization cache, runtime attribute inspection or `Activator` calls.
The repository opts in its libraries through `Library/Directory.Build.props`; tests
and utilities are ordinary consumers.

## Duplicates and ordering

* Disk type names, extensions and transport schemes remain case insensitive.
  Extension normalization matches the existing attribute (periods are removed).
  Duplicate keys throw `ArgumentException`; existing registrations are not replaced.
  Disk-factory conflicts leave all mappings unchanged.
* Explicit file-system registrations append, including repeated instances. Detection
  follows that order. Logical-volume factories retain `ConcurrentBag` semantics and
  unspecified enumeration order.
* Assembly-level registration runs once, including a mix of generated and reflection
  setup. Low-level reflection registration of an unguarded external assembly retains
  its duplicate behavior. Call the assembly-level helper to share the once-only guard.
* Callbacks are not transactions: a failed callback can leave earlier registrations
  in place and remains marked, as in legacy assembly setup. Reentrant assembly setup
  from a factory constructor is ignored.
* Generated factories are sorted by fully qualified type name; dependency entry points
  are also sorted. The former reflection order was unspecified. Applications control
  when their packages/plugins register by ordering their explicit startup calls.
* Disk dictionaries permit concurrent registration/lookups. Enumeration of supported
  names/extensions is a snapshot with no ordering contract.

## Validation

```sh
dotnet build DiscUtils.slnx -c Debug
dotnet test Tests/RegistrationTests/RegistrationTests.csproj -c Debug -f net10.0
dotnet test Tests/SourceGeneratorTests/SourceGeneratorTests.csproj -c Debug
dotnet test Tests/LibraryTests/LibraryTests.csproj -c Debug -f net10.0
dotnet publish Tests/NativeAotSmoke/NativeAotSmoke.csproj -c Release -r linux-x64
```

Run the published `NativeAotSmoke` executable. It first checks that VHD is not
registered before explicit setup, then calls VHD/FAT/LVM registration. It exercises
VHD type/extension lookup and create/open, image-builder lookup, FAT detection/open,
partition/volume discovery, and a handwritten third-party transport registration.

To test package delivery, pack Streams, Core, Vhd, Fat and Lvm into a local feed,
copy the two smoke-project source files outside the repository, and publish with
`-p:UsePackageReferences=true -p:DiscUtilsPackageVersion=<packed-version>` using that
feed. Inspect the packages to confirm that Core contains no registration analyzer or
transitive build target. A C# 7.3 application targeting .NET Framework 4.6 can also
compile ordinary explicit setup calls without running the generator.

This work does not establish that every DiscUtils operation is AOT-compatible.
iSCSI protocol-key reflection and trimming/native interop issues in individual format
implementations remain outside the discovery change. The PR records actual validation
commands, host constraints and remaining limits.
