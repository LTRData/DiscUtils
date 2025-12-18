using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace DiscUtils.SourceGenerator
{
    [Generator]
    public class FactoryGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Debugging helper - uncomment to attach debugger
            // if (!System.Diagnostics.Debugger.IsAttached) System.Diagnostics.Debugger.Launch();

            var classDeclarations = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: IsSyntaxTargetForGeneration,
                    transform: GetSemanticTargetForGeneration)
                .Where(m => m != null);

            var compilationAndClasses = context.CompilationProvider.Combine(classDeclarations.Collect());

            context.RegisterSourceOutput(compilationAndClasses, (spc, source) => Execute(spc, source.Left, source.Right));
        }

        private bool IsSyntaxTargetForGeneration(SyntaxNode node, CancellationToken cancellationToken)
        {
            return node is ClassDeclarationSyntax cds && cds.BaseList != null;
        }

        private INamedTypeSymbol? GetSemanticTargetForGeneration(GeneratorSyntaxContext context, CancellationToken cancellationToken)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;

            if (symbol == null) return null;

            if (InheritsFrom(symbol, "DiscUtils.Vfs.VfsFileSystemFactory") ||
                InheritsFrom(symbol, "DiscUtils.Internal.VirtualDiskFactory") ||
                InheritsFrom(symbol, "DiscUtils.Internal.LogicalVolumeFactory") ||
                InheritsFrom(symbol, "DiscUtils.Internal.VirtualDiskTransport") ||
                InheritsFrom(symbol, "DiscUtils.Partitions.PartitionTableFactory"))
            {
                return symbol;
            }

            return null;
        }

        private bool InheritsFrom(INamedTypeSymbol symbol, string typeName)
        {
            var current = symbol.BaseType;
            while (current != null)
            {
                if (current.ToDisplayString() == typeName)
                {
                    return true;
                }
                current = current.BaseType;
            }
            return false;
        }

        private void Execute(SourceProductionContext context, Compilation compilation, System.Collections.Immutable.ImmutableArray<INamedTypeSymbol?> classes)
        {
            try 
            {
                if (classes.IsDefaultOrEmpty && compilation.AssemblyName != "DiscUtils" && compilation.AssemblyName != "LTRData.DiscUtils")
                {
                    return;
                }

                var distinctClasses = classes.Where(c => c != null).Distinct(SymbolEqualityComparer.Default).Cast<INamedTypeSymbol>().ToList();

                // Generate AssemblyRegistration for libraries
                if (distinctClasses.Any())
                {
                    GenerateAssemblyRegistration(context, compilation, distinctClasses);
                }

                // If this is the main DiscUtils assembly, generate the aggregator
                if (compilation.AssemblyName == "DiscUtils" || compilation.AssemblyName == "LTRData.DiscUtils")
                {
                    GenerateSetupHelper(context, compilation);
                }
            }
            catch (Exception ex)
            {
                // Generate a file with the error so we can see it
                context.AddSource("GeneratorError.g.cs", SourceText.From($"/* Error: {ex.ToString()} */", Encoding.UTF8));
            }
        }

        private void GenerateAssemblyRegistration(SourceProductionContext context, Compilation compilation, List<INamedTypeSymbol> classes)
        {
            var sb = new StringBuilder();
            var assemblyName = compilation.AssemblyName?.Replace(".", "_");
            
            sb.AppendLine("using System;");
            sb.AppendLine("using DiscUtils.Core;");
            sb.AppendLine();
            sb.AppendLine($"namespace {compilation.AssemblyName}");
            sb.AppendLine("{");
            sb.AppendLine($"    public static class AssemblyRegistration_{assemblyName}");
            sb.AppendLine("    {");
            sb.AppendLine("        public static void Register()");
            sb.AppendLine("        {");

            foreach (var classSymbol in classes)
            {
                if (InheritsFrom(classSymbol, "DiscUtils.Vfs.VfsFileSystemFactory"))
                {
                    sb.AppendLine($"            DiscUtils.FileSystemManager.RegisterFileSystems(new {classSymbol.ToDisplayString()}());");
                }
                else if (InheritsFrom(classSymbol, "DiscUtils.Internal.VirtualDiskFactory"))
                {
                    sb.AppendLine($"            DiscUtils.VirtualDiskManager.RegisterVirtualDiskFactory(new {classSymbol.ToDisplayString()}());");
                }
                else if (InheritsFrom(classSymbol, "DiscUtils.Internal.LogicalVolumeFactory"))
                {
                    sb.AppendLine($"            DiscUtils.VolumeManager.RegisterLogicalVolumeFactory(new {classSymbol.ToDisplayString()}());");
                }
                else if (InheritsFrom(classSymbol, "DiscUtils.Partitions.PartitionTableFactory"))
                {
                    sb.AppendLine($"            DiscUtils.Partitions.PartitionTable.RegisterPartitionTableFactory(new {classSymbol.ToDisplayString()}());");
                }
                else if (InheritsFrom(classSymbol, "DiscUtils.Internal.VirtualDiskTransport"))
                {
                    var attributes = classSymbol.GetAttributes().Where(ad => ad.AttributeClass?.ToDisplayString() == "DiscUtils.Internal.VirtualDiskTransportAttribute");
                    foreach(var attr in attributes)
                    {
                        if (attr.ConstructorArguments.Length > 0)
                        {
                            string scheme = attr.ConstructorArguments[0].Value?.ToString();
                            if (!string.IsNullOrEmpty(scheme))
                            {
                                sb.AppendLine($"            DiscUtils.VirtualDiskManager.RegisterVirtualDiskTransport(\"{scheme}\", () => new {classSymbol.ToDisplayString()}());");
                            }
                        }
                    }
                }
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            context.AddSource($"AssemblyRegistration_{assemblyName}.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        private void GenerateSetupHelper(SourceProductionContext context, Compilation compilation)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Reflection;");
            sb.AppendLine();
            sb.AppendLine("namespace DiscUtils.Setup");
            sb.AppendLine("{");
            sb.AppendLine("    public static class GeneratedSetupHelper");
            sb.AppendLine("    {");
            sb.AppendLine("        public static void RegisterFactories()");
            sb.AppendLine("        {");
            sb.AppendLine("            Console.WriteLine(\"GeneratedSetupHelper.RegisterFactories() called.\");");

            foreach (var reference in compilation.References)
            {
                if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assemblySymbol)
                {
                    var assemblyName = assemblySymbol.Name;
                    if ((assemblyName.StartsWith("DiscUtils.") || assemblyName.StartsWith("LTRData.DiscUtils.")) && !assemblyName.EndsWith("SourceGenerator"))
                    {
                        var safeAssemblyName = assemblyName.Replace(".", "_");
                        var typeName = $"{assemblyName}.AssemblyRegistration_{safeAssemblyName}";
                        
                        var typeSymbol = compilation.GetTypeByMetadataName(typeName);
                        
                        if (typeSymbol != null)
                        {
                             sb.AppendLine($"            Console.WriteLine(\"Calling {typeName}.Register()\");");
                             sb.AppendLine($"            {typeName}.Register();");
                        }
                    }
                }
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            context.AddSource("GeneratedSetupHelper.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }
    }
}

