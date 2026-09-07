using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DiscUtils.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace SourceGeneratorTests;

public sealed class GeneratorTests
{
    private const string Api = """
        namespace DiscUtils.Internal {
            public abstract class VirtualDiskFactory { }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class VirtualDiskFactoryAttribute : System.Attribute {
                public VirtualDiskFactoryAttribute(string type, string extensions) { }
            }
        }
        namespace DiscUtils {
            public static class VirtualDiskManager {
                public static void RegisterVirtualDiskFactory(string name, string[] extensions, Internal.VirtualDiskFactory factory) { }
            }
        }
        namespace DiscUtils.Setup {
            public static class SetupHelper {
                public static void RegisterAssembly(System.Reflection.Assembly assembly, System.Action register) { }
            }
        }
        """;

    [Fact]
    public void AttributeAliasesConstantsAndNestedTypesProduceCompilableDirectCalls()
    {
        const string input = """
            using DiskAttribute = DiscUtils.Internal.VirtualDiskFactoryAttribute;
            internal class Container {
                private const string Extensions = ".one,.two";
                [DiskAttribute("Quot\"ed", Extensions)]
                internal sealed class Factory : DiscUtils.Internal.VirtualDiskFactory { }
            }
            """;
        var (result, compilation) = Run(new[] { Api, input });
        Assert.Empty(result.Diagnostics);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var source = Assert.Single(result.GeneratedTrees).ToString();
        Assert.Contains("new global::Container.Factory()", source);
        Assert.Contains("\"one\", \"two\"", source);
        Assert.Contains("\"Quot\\\"ed\"", source);
        Assert.DoesNotContain("Activator", source);
        Assert.DoesNotContain("GetCustomAttribute", source);
    }

    [Theory]
    [InlineData("[DiscUtils.Internal.VirtualDiskFactory(\"A\", \".a\")] abstract class Factory : DiscUtils.Internal.VirtualDiskFactory { }")]
    [InlineData("[DiscUtils.Internal.VirtualDiskFactory(\"A\", \".a\")] class Factory<T> : DiscUtils.Internal.VirtualDiskFactory { }")]
    [InlineData("[DiscUtils.Internal.VirtualDiskFactory(\"A\", \".a\")] class Factory : DiscUtils.Internal.VirtualDiskFactory { private Factory() { } }")]
    [InlineData("[DiscUtils.Internal.VirtualDiskFactory(\"A\", \".a\")] class Factory { }")]
    public void InvalidFactoryReportsActionableError(string declaration)
    {
        var (result, _) = Run(new[] { Api, declaration });
        Assert.Contains(result.Diagnostics, d => d.Id == "DUAOT001" && d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void OutputIsDeterministicWhenSyntaxTreeOrderChanges()
    {
        const string first = "[DiscUtils.Internal.VirtualDiskFactory(\"A\", \".a\")] class A : DiscUtils.Internal.VirtualDiskFactory { }";
        const string last = "[DiscUtils.Internal.VirtualDiskFactory(\"Z\", \".z\")] class Z : DiscUtils.Internal.VirtualDiskFactory { }";
        var left = Run(new[] { Api, first, last }).Result.GeneratedTrees.Single().ToString();
        var right = Run(new[] { last, Api, first }).Result.GeneratedTrees.Single().ToString();
        Assert.Equal(left, right);
        Assert.True(left.IndexOf("new global::A()", StringComparison.Ordinal) < left.IndexOf("new global::Z()", StringComparison.Ordinal));
    }

    [Fact]
    public void UnattributedDerivedTypesAndUnrelatedSameNamedAttributesAreIgnored()
    {
        const string input = """
            class Unmarked : DiscUtils.Internal.VirtualDiskFactory { }
            class VirtualDiskFactoryAttribute : System.Attribute { }
            [VirtualDiskFactory] class Unrelated : DiscUtils.Internal.VirtualDiskFactory { }
            """;
        var (result, _) = Run(new[] { Api, input });
        Assert.Empty(result.GeneratedTrees);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ConsumerDoesNotGenerateAnythingWithoutLibraryOptIn()
    {
        var library = CSharpCompilation.Create("DiscUtils.Example", new[] { CSharpSyntaxTree.ParseText(
            "namespace DiscUtils.Example { public static class Formats { public static void Register() { } } }") },
            References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        Assert.True(library.Emit(image).Success);
        var reference = MetadataReference.CreateFromImage(image.ToArray());
        var (result, compilation) = Run(new[] { "class App { }" }, false, reference);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.GeneratedTrees);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void PrivateLibraryGetsExplicitRegistrationWithoutModuleInitializationOrCSharp9()
    {
        var (result, compilation) = Run(new[] {
            Api, "[DiscUtils.Internal.VirtualDiskFactory(\"PRIVATE\", \".private\")] class Factory : DiscUtils.Internal.VirtualDiskFactory { }"
        }, languageVersion: LanguageVersion.CSharp7_3, assemblyName: "MyLibrary");
        Assert.Empty(result.Diagnostics);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var source = Assert.Single(result.GeneratedTrees).ToString();
        Assert.Contains("new global::Factory()", source);
        Assert.DoesNotContain("ModuleInitializer", source);
        var entryPoint = compilation.GetTypeByMetadataName("MyLibrary.Formats")!;
        Assert.Equal(Accessibility.Public, entryPoint.DeclaredAccessibility);
        Assert.Single(entryPoint.GetMembers("Register").OfType<IMethodSymbol>());
        Assert.Empty(entryPoint.StaticConstructors);
    }

    private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p)).ToArray();

    private static (GeneratorDriverRunResult Result, Compilation Compilation) Run(string[] sources, bool isLibrary = true,
        MetadataReference? extraReference = null, LanguageVersion languageVersion = LanguageVersion.Preview,
        string assemblyName = "DiscUtils.Test")
    {
        var options = new CSharpParseOptions(languageVersion);
        var references = extraReference == null ? References : References.Append(extraReference);
        var compilation = CSharpCompilation.Create(assemblyName, sources.Select(s => CSharpSyntaxTree.ParseText(s, options)),
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new FormatRegistrationGenerator().AsSourceGenerator() },
            parseOptions: options, optionsProvider: new OptionsProvider(isLibrary));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (driver.GetRunResult(), output);
    }

    private sealed class OptionsProvider(bool isLibrary) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(isLibrary);
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
    }

    private sealed class Options(bool isLibrary) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            value = isLibrary ? "true" : "false";
            return key == "build_property.DiscUtilsGenerateRegistration";
        }
    }
}
