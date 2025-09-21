using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace FluentPermissions.Tests;

internal static class TestCompilationHelper
{
    private static readonly MetadataReference[] BasicReferences =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ImmutableArray).Assembly.Location),
        // Reference the FluentPermissions.Core assembly so registrar code can compile
        MetadataReference.CreateFromFile(typeof(Core.Builder.PermissionBuilder<,>).Assembly.Location)
    ];

    public static CSharpCompilation CreateCompilation(string assemblyName, params string[] sources)
    {
        var trees = sources.Select(s => CSharpSyntaxTree.ParseText(s));
        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: trees,
            references: BasicReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    public static GeneratorDriver CreateDriver()
    {
        var generator = new PermissionSourceGenerator();
        return CSharpGeneratorDriver.Create(generator);
    }
}
