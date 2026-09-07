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
DiscUtils.Core.Formats.Register(); // RAW disks, file transport, partition and dynamic-volume providers
DiscUtils.Vhd.Formats.Register();
DiscUtils.Fat.Formats.Register();
```

Each entry point registers **only providers implemented by that assembly**. VHD
registration does not register Core, FAT or any other referenced library. Register
Core explicitly when using its RAW disk or file transport through the generic disk
APIs. No dependency registrations are inferred from assembly references.

The existing setup helpers compose the relevant generated entry points explicitly,
with Core first, preserving their original provider selection and assembly order:

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

Meta-packages and libraries with no discovery attributes produce no `Formats` class.
Their existing setup helpers are the public composition API. References used for an
implementation do not expand a helper's registration scope: for example,
`SetupContainers()` does not activate the optical-disc-sharing transport merely
because the container package references that library.

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

`VirtualDiskTransport`, `VirtualDiskTransportAttribute`, `LogicalVolumeFactory` and
`LogicalVolumeFactoryAttribute` are supported public extension points, including for
custom transports and experimental logical-volume mappings. Their existing
`DiscUtils.Internal` namespace does not restrict third-party use. Explicit transport
and logical-volume registration is supported alongside the built-in providers.
`LogicalVolumeInfo`'s public constructor lets external factories return mapped
volumes with their own content-opening delegate. Partition-table factories and their
registration methods remain internal; their existing discovery is confined to Core.

A handwritten library entry point can use the assembly identity guard:

```csharp
public static void Register()
{
    DiscUtils.Setup.SetupHelper.RegisterAssembly(typeof(MyDiskFactory).Assembly, () =>
    {
        VirtualDiskManager.RegisterVirtualDiskFactory(
            "MYDISK", new[] { ".mydisk" }, new MyDiskFactory());
    });
}
```

The application composes this entry point with Core or other providers it needs,
just as it does with generated library registration.

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
`MyLibrary.Formats.Register()`. The generated namespace is derived from the assembly
name by sanitizing each dot-separated segment: valid C# identifier characters are
preserved, other characters become `_`, and `_` is prefixed when the result cannot
start an identifier or is a reserved C# keyword. Empty segments become `_`.
For example, `Acme.DiscUtils-Plugin` becomes `Acme.DiscUtils_Plugin`, `123.Tools`
becomes `_123.Tools`, and `Acme.class` becomes `Acme._class`. Contextual keywords
that are valid namespace identifiers remain unchanged. No assembly rename is needed.

Only the generated namespace/type name is sanitized. Registration still uses
`typeof(Formats).Assembly`, preserving the actual runtime assembly identity; package
IDs, format names and registry keys are unaffected. Sanitization collisions are
accepted without hashing or collision detection; each generated type belongs to its
own assembly. The application explicitly calls the generated method during startup.
Generation requires a Roslyn 4.8+ compiler in the format library build only; its output
also compiles in C# 7.3. The consuming application needs only the compiled library.
There is no separately published generator package in this change.

The generated method calls the common assembly registration guard directly. It adds
no static initialization cache, runtime attribute inspection or `Activator` calls.
It does not inspect the consuming application or referenced libraries for providers
or registration entry points. The repository keeps a single library-wide opt-in in
`Library/Directory.Build.props` to avoid a project list to maintain. Libraries with no
discovery attributes emit nothing, even if they reference format libraries. Tests and
utilities are ordinary consumers except the explicitly opted-in private-library test
fixture.

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
* The assembly guard is keyed by full assembly name and marks it before invoking the
  callback. Recursive registration from within the callback is ignored. Callbacks are
  not transactions: if one throws, the exception propagates, earlier registrations
  remain and the assembly stays marked. Subsequent generated, handwritten or
  assembly-level reflection registration does **not** retry it. This preserves the
  legacy reflection helper's failure semantics; register additional providers through
  the ordinary registry APIs if needed.
* Generated factories within an assembly are sorted by fully qualified type name;
  the former reflection order was unspecified. Setup helpers explicitly retain their
  original assembly order. Applications control when packages/plugins register by
  ordering their startup calls.
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

Run the published executable twice, in separate processes:

```sh
./NativeAotSmoke
./NativeAotSmoke --containers
```

Both paths check that VHD is absent before setup. The direct path registers VHD
twice and verifies that **only VHD** was registered, then explicitly registers Core,
FAT and LVM. The helper path calls `SetupContainers()` twice and uses its VHD/Core
registrations, with a separate FAT registration for the file-system check. Both
exercise VHD type/extension lookup and create/open, image-builder lookup, FAT
detection/open, partition/volume discovery and a handwritten third-party transport.

Registration tests cover successful repeated/concurrent registration, reentrance,
both explicit/reflection orders and persistent partial failure. A separate private
library builds its own generated entry point and proves it runs once when followed
by reflection discovery. The reflection-only plugin fixture has no generator or
friend access and exercises disk, file-system, transport and logical-volume APIs.

To test package delivery, pack Containers, Fat and their project dependencies into a local feed,
copy the two smoke-project source files outside the repository, and publish with
`-p:UsePackageReferences=true -p:DiscUtilsPackageVersion=<packed-version>` using that
feed. Inspect the packages to confirm that Core contains no registration analyzer or
transitive build target. A C# 7.3 application targeting .NET Framework 4.6 can also
compile ordinary explicit setup calls without running the generator.

This work does not establish that every DiscUtils operation is AOT-compatible.
iSCSI protocol-key reflection and trimming/native interop issues in individual format
implementations remain outside the discovery change. The PR records actual validation
commands, host constraints and remaining limits.
