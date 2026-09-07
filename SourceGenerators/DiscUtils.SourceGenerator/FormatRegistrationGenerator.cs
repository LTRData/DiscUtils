using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DiscUtils.SourceGenerator;

/// <summary>Compiles the existing discovery attributes into ordinary registry calls.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class FormatRegistrationGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor InvalidFactory = new(
        "DUAOT001", "Factory cannot be registered directly", "{0}", "DiscUtils",
        DiagnosticSeverity.Error, true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var disks = Find(context, "DiscUtils.Internal.VirtualDiskFactoryAttribute");
        var transports = Find(context, "DiscUtils.Internal.VirtualDiskTransportAttribute");
        var fileSystems = Find(context, "DiscUtils.Vfs.VfsFileSystemFactoryAttribute");
        var partitions = Find(context, "DiscUtils.Partitions.PartitionTableFactoryAttribute");
        var volumes = Find(context, "DiscUtils.Internal.LogicalVolumeFactoryAttribute");
        var registrations = disks.Combine(transports).Combine(fileSystems).Combine(partitions).Combine(volumes)
            .Select((x, _) => x.Left.Left.Left.Left.AddRange(x.Left.Left.Left.Right)
                .AddRange(x.Left.Left.Right).AddRange(x.Left.Right).AddRange(x.Right));
        var settings = context.AnalyzerConfigOptionsProvider.Select((options, cancellationToken) =>
            options.GlobalOptions.TryGetValue("build_property.DiscUtilsGenerateRegistration", out var value)
                && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

        var assemblyName = context.CompilationProvider.Select((compilation, _) => compilation.AssemblyName!);
        context.RegisterSourceOutput(assemblyName.Combine(registrations).Combine(settings),
            (output, input) => Emit(output, input.Left.Left, input.Left.Right, input.Right));
    }

    private static IncrementalValueProvider<ImmutableArray<Registration>> Find(
        IncrementalGeneratorInitializationContext context, string attribute)
        => context.SyntaxProvider.ForAttributeWithMetadataName(attribute,
            static (node, _) => node is TypeDeclarationSyntax,
            (syntax, _) => ReadRegistration(syntax, attribute)).Collect();

    private static Registration ReadRegistration(GeneratorAttributeSyntaxContext context, string attribute)
    {
        var type = (INamedTypeSymbol)context.TargetSymbol;
        var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var baseName = attribute.Substring(0, attribute.Length - "Attribute".Length);
        var baseType = context.SemanticModel.Compilation.GetTypeByMetadataName(baseName);
        var inherits = false;
        for (var current = type.BaseType; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType)) inherits = true;
        }
        var accessible = true;
        for (var current = type; current != null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public &&
                current.DeclaredAccessibility != Accessibility.Internal &&
                current.DeclaredAccessibility != Accessibility.ProtectedOrInternal) accessible = false;
        }
        if (!inherits || type.IsAbstract || type.IsGenericType || !accessible ||
            !type.InstanceConstructors.Any(c => c.Parameters.Length == 0 &&
                (c.DeclaredAccessibility == Accessibility.Public || c.DeclaredAccessibility == Accessibility.Internal ||
                 c.DeclaredAccessibility == Accessibility.ProtectedOrInternal)))
        {
            return new Registration(name, string.Empty, context.TargetNode.GetLocation(),
                name + " must be a non-abstract, non-generic " + baseName +
                " accessible from its assembly, with an accessible parameterless constructor.");
        }

        var calls = new StringBuilder();
        foreach (var data in context.Attributes)
        {
            var instance = "new " + name + "()";
            switch (attribute)
            {
                case "DiscUtils.Internal.VirtualDiskFactoryAttribute":
                    if (data.ConstructorArguments.Length != 2 ||
                        data.ConstructorArguments[0].Value is not string diskType ||
                        data.ConstructorArguments[1].Value is not string extensions)
                        return InvalidMetadata();
                    // Exactly the normalization performed by VirtualDiskFactoryAttribute.
                    var values = extensions.Replace(".", string.Empty).Split(',');
                    calls.Append("global::DiscUtils.VirtualDiskManager.RegisterVirtualDiskFactory(")
                        .Append(Literal(diskType)).Append(", new string[] { ")
                        .Append(string.Join(", ", values.Select(Literal)))
                        .Append(" }, ").Append(instance).AppendLine(");");
                    break;
                case "DiscUtils.Internal.VirtualDiskTransportAttribute":
                    if (data.ConstructorArguments.Length != 1 || data.ConstructorArguments[0].Value is not string scheme)
                        return InvalidMetadata();
                    calls.Append("global::DiscUtils.VirtualDiskManager.RegisterVirtualDiskTransport(")
                        .Append(Literal(scheme)).Append(", () => ").Append(instance).AppendLine(");");
                    break;
                case "DiscUtils.Vfs.VfsFileSystemFactoryAttribute":
                    calls.Append("global::DiscUtils.FileSystemManager.RegisterFileSystems(").Append(instance).AppendLine(");");
                    break;
                case "DiscUtils.Partitions.PartitionTableFactoryAttribute":
                    calls.Append("global::DiscUtils.Partitions.PartitionTable.RegisterPartitionTableFactory(").Append(instance).AppendLine(");");
                    break;
                case "DiscUtils.Internal.LogicalVolumeFactoryAttribute":
                    calls.Append("global::DiscUtils.VolumeManager.RegisterLogicalVolumeFactory(").Append(instance).AppendLine(");");
                    break;
            }
        }
        return new Registration(name, calls.ToString(), null, null);

        Registration InvalidMetadata() => new(name, string.Empty, context.TargetNode.GetLocation(),
            name + " has invalid discovery attribute arguments.");
    }

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, true);

    private static void Emit(SourceProductionContext output, string assemblyName,
        ImmutableArray<Registration> registrations, bool isLibrary)
    {
        if (!isLibrary || registrations.IsEmpty) return;
        foreach (var registration in registrations.Where(r => r.Error != null))
            output.ReportDiagnostic(Diagnostic.Create(InvalidFactory, registration.Location, registration.Error));
        if (registrations.Any(r => r.Error != null)) return;

        var source = new StringBuilder("// <auto-generated/>\n");
        var setupName = assemblyName + ".Formats";
        source.Append("namespace ").Append(assemblyName).AppendLine(" {");
        source.AppendLine("/// <summary>Registers only the formats and providers implemented by this assembly.</summary>");
        source.AppendLine("public static class Formats {");
        source.AppendLine("/// <summary>Explicitly registers formats once, without reflection.</summary>");
        source.AppendLine("public static void Register() {");

        source.Append("global::DiscUtils.Setup.SetupHelper.RegisterAssembly(typeof(global::")
            .Append(setupName).AppendLine(").Assembly, () => {");
        foreach (var registration in registrations.OrderBy(r => r.Name, StringComparer.Ordinal)) source.Append(registration.Calls);
        source.AppendLine("});");
        source.AppendLine("}\n}\n}");

        output.AddSource("DiscUtils.FormatRegistration.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
    }

    private sealed class Registration
    {
        internal Registration(string name, string calls, Location? location, string? error)
        { Name = name; Calls = calls; Location = location; Error = error; }
        internal string Name { get; }
        internal string Calls { get; }
        internal Location? Location { get; }
        internal string? Error { get; }
    }
}
