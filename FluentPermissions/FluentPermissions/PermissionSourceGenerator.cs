using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FluentPermissions;

[Generator(LanguageNames.CSharp)]
public sealed class PermissionSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var registrarProvider = context.SyntaxProvider
            .CreateSyntaxProvider(static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => TryGetRegistrar(ctx)).Where(static r => r is not null)
            .Select(static (r, _) => r!);

        var combined = context.CompilationProvider.Combine(registrarProvider.Collect());

        context.RegisterSourceOutput(combined, static (spc, tuple) =>
        {
            var (compilation, registrars) = tuple;
            if (registrars.Length == 0)
                return;

            var analyzer = new Analyzer(compilation);
            var models = analyzer.Analyze(registrars);

            // Validate option types consistency
            if (models.Diagnostics.Length > 0)
            {
                foreach (var d in models.Diagnostics) spc.ReportDiagnostic(d);

                if (models.HasFatal) return;
            }

            spc.AddSource("FluentPermissions.g.Models.cs", SourceBuilders.BuildModels(models));
            spc.AddSource("AppPermissions.g.cs", SourceBuilders.BuildApp(models));
        });
    }

    private static RegistrarInfo? TryGetRegistrar(GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is not ClassDeclarationSyntax classDecl) return null;
        var model = ctx.SemanticModel;
        var symbol = model.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        if (symbol is null) return null;

        var genericSig = "FluentPermissions.Core.Abstractions.IPermissionRegistrar<TGroupOptions, TPermissionOptions>";
        var nonGenericSig = "FluentPermissions.Core.Abstractions.IPermissionRegistrar";
        var generic = symbol.AllInterfaces.FirstOrDefault(i => i.OriginalDefinition.ToDisplayString() == genericSig);
        if (generic is not null)
            return new RegistrarInfo(symbol, generic, false);

        var nonGeneric = symbol.AllInterfaces.FirstOrDefault(i => i.ToDisplayString() == nonGenericSig);
        return nonGeneric is not null ? new RegistrarInfo(symbol, null, true) : null;
    }

    internal static string EscapeString(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (char.IsControl(ch))
                    {
                        sb.Append("\\u");
                        sb.Append(((int)ch).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(ch);
                    }

                    break;
            }

        return sb.ToString();
    }
}