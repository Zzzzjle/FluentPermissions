using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
                foreach (var d in models.Diagnostics)
                {
                    spc.ReportDiagnostic(d);
                }

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
        var symbol = model.GetDeclaredSymbol(classDecl);
        if (symbol is null) return null;

        var registrarInterface = symbol.AllInterfaces.FirstOrDefault(i => i.OriginalDefinition.ToDisplayString() ==
                                                                          "FluentPermissions.Core.Abstractions.IPermissionRegistrar<TGroupOptions, TPermissionOptions>");
        return registrarInterface is null ? null : new RegistrarInfo(symbol, registrarInterface);
    }

    private sealed class Analyzer(Compilation compilation)
    {
        public Model Analyze(ImmutableArray<RegistrarInfo> registrars)
        {
            var diags = ImmutableArray.CreateBuilder<Diagnostic>();

            // Ensure all registrars use consistent option types
            ITypeSymbol? groupOptions = null;
            ITypeSymbol? permOptions = null;

            var allGroups = new List<GroupDef>();

            foreach (var reg in registrars)
            {
                var @interface = reg.Interface;
                var g = @interface.TypeArguments[0];
                var p = @interface.TypeArguments[1];
                if (groupOptions is null)
                {
                    groupOptions = g;
                    permOptions = p;
                }
                else
                {
                    if (!SymbolEqualityComparer.Default.Equals(groupOptions, g) ||
                        !SymbolEqualityComparer.Default.Equals(permOptions!, p))
                    {
                        diags.Add(Diagnostic.Create(Diagnostics.InconsistentOptionsTypes,
                            reg.Symbol.Locations.FirstOrDefault()));
                        return new Model(compilation, groupOptions, permOptions!, ImmutableArray<GroupDef>.Empty,
                            diags.ToImmutable(), hasFatal: true);
                    }
                }

                var registerMethod = reg.Symbol.GetMembers().OfType<IMethodSymbol>()
                    .FirstOrDefault(m => m.Name == "Register" && m.Parameters.Length == 1);
                if (registerMethod is null)
                {
                    diags.Add(Diagnostic.Create(Diagnostics.MissingRegisterMethod,
                        reg.Symbol.Locations.FirstOrDefault()));
                    continue;
                }

                // Try to find the method syntax
                foreach (var decl in registerMethod.DeclaringSyntaxReferences)
                {
                    if (decl.GetSyntax() is not MethodDeclarationSyntax mds) continue;
                    var groups = ParseRegisterBody(mds);
                    allGroups.AddRange(groups);
                }
            }

            return new Model(compilation, groupOptions!, permOptions!, allGroups.ToImmutableArray(),
                diags.ToImmutable(), hasFatal: false);
        }

        private IEnumerable<GroupDef> ParseRegisterBody(MethodDeclarationSyntax methodSyntax)
        {
            var semanticModel = compilation.GetSemanticModel(methodSyntax.SyntaxTree);
            if (methodSyntax.Body is null && methodSyntax.ExpressionBody is null)
                yield break;

            var root = methodSyntax.Body ?? (SyntaxNode)methodSyntax.ExpressionBody!;
            var groupsRoot = new List<GroupDef>();

            foreach (var stmt in root.DescendantNodes().OfType<ExpressionStatementSyntax>())
            {
                if (stmt.Expression is not InvocationExpressionSyntax invRoot) continue;
                var calls = FlattenCalls(invRoot).ToList();
                if (!calls.Any(c => c.Expression is MemberAccessExpressionSyntax
                    {
                        Name.Identifier.Text: "DefineGroup"
                    }))
                    continue;

                var stack = new Stack<GroupDef>();

                foreach (var call in calls)
                {
                    if (call.Expression is not MemberAccessExpressionSyntax maes) continue;
                    var methodName = maes.Name.Identifier.Text;
                    switch (methodName)
                    {
                        case "DefineGroup":
                        {
                            var args = call.ArgumentList.Arguments;
                            if (args.Count == 0) continue;
                            var parsed = ParseGroupOrPermissionArguments(semanticModel, args);
                            if (parsed.LogicalName is null) continue;
                            var parent = stack.Count == 0 ? null : stack.Peek();
                            var grp = GetOrAddGroup(parent, parsed.LogicalName, parsed.Props);
                            grp.DisplayName ??= parsed.DisplayName ?? parsed.LogicalName;
                            grp.Description ??= parsed.Description;
                            stack.Push(grp);

                            // If this DefineGroup call uses the builder-lambda overload, process its body here
                            var methodSymbol = semanticModel.GetSymbolInfo(call).Symbol as IMethodSymbol;
                            if (methodSymbol is not null)
                            {
                                var builderLambdaArg = GetBuilderLambdaArgumentIfAny(methodSymbol, call);
                                if (builderLambdaArg is not null)
                                {
                                    ProcessBuilderLambda(grp, builderLambdaArg, semanticModel);
                                    // Close this group scope immediately for builder-lambda overloads
                                    if (stack.Count > 0) stack.Pop();
                                }
                            }
                            break;
                        }
                        case "WithOptions" when stack.Count == 0:
                            // Without a current group, ignore.
                            break;
                        case "WithOptions":
                        {
                            // Merge options assignments into the current group's properties
                            var args = call.ArgumentList.Arguments;
                            if (args.Count > 0)
                            {
                                var cur = stack.Peek();
                                ExtractAssignmentsFromLambda(semanticModel, args[0].Expression, cur.Props);
                            }
                            break;
                        }
                        case "AddPermission" when stack.Count == 0:
                            continue; // skip if no current group
                        case "AddPermission":
                        {
                            var args = call.ArgumentList.Arguments;
                            if (args.Count == 0) continue;
                            var parsed = ParseGroupOrPermissionArguments(semanticModel, args);
                            if (parsed.LogicalName is null) continue;
                            AddPermissionIfMissing(stack.Peek(), parsed.LogicalName, parsed.DisplayName, parsed.Description, parsed.Props);
                            break;
                        }
                        // 'Then' style removed: no-op if seen in legacy code
                    }
                }
            }

            foreach (var g in groupsRoot)
                yield return g;
            yield break;

            void AddPermissionIfMissing(GroupDef parent, string name, string? displayName, string? description, Dictionary<string, ConstValue> props)
                => AddOrUpdatePermission(parent, name, displayName, description, props);

            // Helper for de-duplication
            GroupDef GetOrAddGroup(GroupDef? parent, string name, Dictionary<string, ConstValue> props)
            {
                if (parent is null)
                {
                    var existing =
                        groupsRoot.FirstOrDefault(g => string.Equals(g.LogicalName, name, StringComparison.Ordinal));
                    if (existing is not null) return existing;
                    var created = new GroupDef(name, null, null, props, [], []);
                    groupsRoot.Add(created);
                    return created;
                }
                else
                {
                    var existing =
                        parent.Children.FirstOrDefault(g => string.Equals(g.LogicalName, name, StringComparison.Ordinal));
                    if (existing is not null) return existing;
                    var created = new GroupDef(name, null, null, props, [], []);
                    parent.Children.Add(created);
                    return created;
                }
            }
        }

        private static ArgumentSyntax? GetBuilderLambdaArgumentIfAny(IMethodSymbol methodSymbol, InvocationExpressionSyntax call)
        {
            // find parameter of type Action<...PermissionGroupBuilder...>
            int paramIndex = -1;
            for (int i = 0; i < methodSymbol.Parameters.Length; i++)
            {
                var p = methodSymbol.Parameters[i].Type as INamedTypeSymbol;
                if (p is null) continue;
                if (!string.Equals(p.Name, "Action", StringComparison.Ordinal)) continue;
                if (p.TypeArguments.Length != 1) continue;
                var targ = p.TypeArguments[0];
                var targName = targ.ToDisplayString();
                if (targName.IndexOf("PermissionGroupBuilder", StringComparison.Ordinal) >= 0)
                {
                    paramIndex = i;
                    break;
                }
            }

            if (paramIndex < 0) return null;
            var args = call.ArgumentList.Arguments;
            if (paramIndex >= args.Count) return null;
            return args[paramIndex];
        }

        private void ProcessBuilderLambda(GroupDef current, ArgumentSyntax lambdaArg, SemanticModel semanticModel)
        {
            var expr = lambdaArg.Expression;
            CSharpSyntaxNode? body = expr switch
            {
                ParenthesizedLambdaExpressionSyntax ples => ples.Body,
                SimpleLambdaExpressionSyntax sles => sles.Body,
                _ => null
            };
            if (body is null) return;

            // Local processing stack: start with current group
            var stack = new Stack<GroupDef>();
            stack.Push(current);

            void HandleCall(InvocationExpressionSyntax call)
            {
                if (call.Expression is not MemberAccessExpressionSyntax maes) return;
                var methodName = maes.Name.Identifier.Text;
                switch (methodName)
                {
                    case "WithOptions" when stack.Count > 0:
                    {
                        var args = call.ArgumentList.Arguments;
                        if (args.Count > 0)
                        {
                            var cur = stack.Peek();
                            ExtractAssignmentsFromLambda(semanticModel, args[0].Expression, cur.Props);
                        }
                        break;
                    }
                    case "DefineGroup":
                    {
                        var args = call.ArgumentList.Arguments;
                        if (args.Count == 0) break;
                        var parsed = ParseGroupOrPermissionArguments(semanticModel, args);
                        if (parsed.LogicalName is null) break;
                        var parent = stack.Peek();
                        var grp = parent.Children.FirstOrDefault(g => string.Equals(g.LogicalName, parsed.LogicalName, StringComparison.Ordinal))
                                  ?? new GroupDef(parsed.LogicalName, null, null, new Dictionary<string, ConstValue>(StringComparer.Ordinal), new List<PermissionDef>(), new List<GroupDef>());
                        if (!parent.Children.Contains(grp)) parent.Children.Add(grp);
                        grp.DisplayName ??= parsed.DisplayName ?? parsed.LogicalName;
                        grp.Description ??= parsed.Description;
                        foreach (var kv in parsed.Props) grp.Props[kv.Key] = kv.Value;

                        stack.Push(grp);

                        var methodSymbol = semanticModel.GetSymbolInfo(call).Symbol as IMethodSymbol;
                        if (methodSymbol is not null)
                        {
                            var builderLambda = GetBuilderLambdaArgumentIfAny(methodSymbol, call);
                            if (builderLambda is not null)
                            {
                                ProcessBuilderLambda(grp, builderLambda, semanticModel);
                            }
                        }
                        break;
                    }
                    case "AddPermission" when stack.Count > 0:
                    {
                        var args = call.ArgumentList.Arguments;
                        if (args.Count == 0) break;
                        var parsed = ParseGroupOrPermissionArguments(semanticModel, args);
                        if (parsed.LogicalName is null) break;
                        AddOrUpdatePermission(stack.Peek(), parsed.LogicalName, parsed.DisplayName, parsed.Description, parsed.Props);
                        break;
                    }
                    // 'Then' style removed: ignore if encountered in lambda body
                }
            }

            switch (body)
            {
                case BlockSyntax block:
                {
                    foreach (var stmt in block.Statements.OfType<ExpressionStatementSyntax>())
                    {
                        if (stmt.Expression is InvocationExpressionSyntax inv)
                        {
                            var calls = FlattenCalls(inv);
                            foreach (var c in calls)
                            {
                                HandleCall(c);
                            }
                        }
                    }
                    break;
                }
                case ExpressionSyntax exprBody:
                {
                    if (exprBody is InvocationExpressionSyntax inv)
                    {
                        var calls = FlattenCalls(inv);
                        foreach (var c in calls)
                        {
                            HandleCall(c);
                        }
                    }
                    break;
                }
            }
        }

        private (string? LogicalName, string? DisplayName, string? Description, Dictionary<string, ConstValue> Props) ParseGroupOrPermissionArguments(
            SemanticModel semanticModel,
            SeparatedSyntaxList<ArgumentSyntax> args)
        {
            string? logicalName = null;
            string? displayName = null;
            string? description = null;
            var props = new Dictionary<string, ConstValue>(StringComparer.Ordinal);

            if (args.Count > 0)
            {
                logicalName = GetConstString(semanticModel, args[0].Expression);
            }

            for (int i = 1; i < args.Count; i++)
            {
                var expr = args[i].Expression;
                // string literal branches
                var str = GetConstString(semanticModel, expr);
                if (str is not null)
                {
                    if (displayName is null) { displayName = str; continue; }
                    if (description is null) { description = str; continue; }
                }
                // options lambda
                ExtractAssignmentsFromLambda(semanticModel, expr, props);
            }

            return (logicalName, displayName, description, props);
        }

        private static IEnumerable<InvocationExpressionSyntax> FlattenCalls(InvocationExpressionSyntax root)
        {
            var stack = new Stack<InvocationExpressionSyntax>();
            ExpressionSyntax cur = root;
            while (cur is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax maes } inv)
            {
                stack.Push(inv);
                cur = maes.Expression;
            }

            return
                stack; // Stack enumerates from last pushed to first, which in our loop yields earliest-to-latest calls
        }

        private static void ExtractAssignmentsFromLambda(SemanticModel semanticModel, ExpressionSyntax expr,
            Dictionary<string, ConstValue> into)
        {
            switch (expr)
            {
                // Expect a simple lambda like: options => { options.Prop = <literal>; ... }
                // or: o => o.Prop = 123
                case ParenthesizedLambdaExpressionSyntax syntax:
                {
                    var paramName = syntax.ParameterList.Parameters.FirstOrDefault()?.Identifier.Text;
                    if (paramName is null) return;
                    ExtractAssignmentsFromLambdaBody(semanticModel, paramName, syntax.Body, into);
                    break;
                }
                case SimpleLambdaExpressionSyntax sles:
                {
                    var paramName = sles.Parameter.Identifier.Text;
                    ExtractAssignmentsFromLambdaBody(semanticModel, paramName, sles.Body, into);
                    break;
                }
            }
        }

        private static void ExtractAssignmentsFromLambdaBody(SemanticModel semanticModel, string paramName,
            CSharpSyntaxNode body, Dictionary<string, ConstValue> into)
        {
            switch (body)
            {
                case BlockSyntax block:
                {
                    foreach (var stmt in block.Statements.OfType<ExpressionStatementSyntax>())
                    {
                        if (stmt.Expression is AssignmentExpressionSyntax assign)
                        {
                            TryCaptureAssignment(semanticModel, paramName, assign, into);
                        }
                    }

                    break;
                }
                case ExpressionSyntax expr:
                {
                    if (expr is AssignmentExpressionSyntax assign)
                    {
                        TryCaptureAssignment(semanticModel, paramName, assign, into);
                    }

                    break;
                }
            }
        }

        private static void TryCaptureAssignment(SemanticModel semanticModel, string paramName,
            AssignmentExpressionSyntax assign, Dictionary<string, ConstValue> into)
        {
            if (assign.Left is not MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax id } maes ||
                id.Identifier.Text != paramName) return;
            var propName = maes.Name.Identifier.Text;
            var value = GetConstValue(semanticModel, assign.Right);
            if (value is not null)
            {
                into[propName] = value;
            }
        }

        private static string? GetConstString(SemanticModel semanticModel, ExpressionSyntax expr)
        {
            var cv = GetConstValue(semanticModel, expr);
            return cv is { Kind: ConstKind.String, Value: string s } ? s : null;
        }

        private static ConstValue? GetConstValue(SemanticModel semanticModel, ExpressionSyntax expr)
        {
            expr = (expr as InvocationExpressionSyntax)?.Expression ?? expr;
            var c = semanticModel.GetConstantValue(expr);
            if (!c.HasValue)
                return expr.IsKind(SyntaxKind.NullLiteralExpression) ? new ConstValue(ConstKind.Null, null) : null;
            return c.Value switch
            {
                string s => new ConstValue(ConstKind.String, s),
                bool b => new ConstValue(ConstKind.Bool, b),
                int i => new ConstValue(ConstKind.Int, i),
                double d => new ConstValue(ConstKind.Double, d),
                _ => expr.IsKind(SyntaxKind.NullLiteralExpression) ? new ConstValue(ConstKind.Null, null) : null
            };
        }
    }

    private static void AddOrUpdatePermission(GroupDef parent, string name, string? displayName, string? description, Dictionary<string, ConstValue> props)
    {
        var existing = parent.Permissions.FirstOrDefault(p => string.Equals(p.LogicalName, name, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (existing.DisplayName is null && displayName is not null) existing.DisplayName = displayName;
            if (existing.Description is null && description is not null) existing.Description = description;
            foreach (var kv in props) existing.Props[kv.Key] = kv.Value;
            return;
        }
        parent.Permissions.Add(new PermissionDef(name, displayName, description, props));
    }

    private static class Diagnostics
    {
        public static readonly DiagnosticDescriptor InconsistentOptionsTypes = new(
            id: "FP001",
            title: "Inconsistent option types across registrars",
            messageFormat:
            "All IPermissionRegistrar implementations must use the same TGroupOptions and TPermissionOptions types within one project",
            category: "FluentPermissions",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MissingRegisterMethod = new(
            id: "FP002",
            title: "Registrar missing Register method",
            messageFormat: "Type implements IPermissionRegistrar but has no valid Register method",
            category: "FluentPermissions",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
    }

    private sealed class RegistrarInfo(INamedTypeSymbol symbol, INamedTypeSymbol @interface)
    {
        public INamedTypeSymbol Symbol { get; } = symbol;
        public INamedTypeSymbol Interface { get; } = @interface;
    }

    private enum ConstKind
    {
        String,
        Bool,
        Int,
        Double,
        Null
    }

    private sealed class ConstValue(ConstKind kind, object? value)
    {
        public ConstKind Kind { get; } = kind;
        public object? Value { get; } = value;

        public string ToCSharpLiteral()
        {
            return Kind switch
            {
                ConstKind.String => EscapeString((string?)Value),
                ConstKind.Bool => (bool)Value! ? "true" : "false",
                ConstKind.Int => ((int)Value!).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ConstKind.Double => ((double)Value!).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ConstKind.Null => "null",
                _ => "default"
            };
        }
    }

    private static string EscapeString(string? s)
    {
        if (s is null) return "null";
        var sb = new StringBuilder();
        sb.Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (char.IsControl(ch)) sb.AppendFormat("\\u{0:X4}", (int)ch);
                    else sb.Append(ch);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    private sealed class OptionProperty(string name, ITypeSymbol type)
    {
        public string Name { get; } = name;
        private ITypeSymbol Type { get; } = type;

        private static readonly SymbolDisplayFormat FullyQualifiedWithNullable = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                                  SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
                                  SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        public string TypeName => Type.ToDisplayString(FullyQualifiedWithNullable);
        public string DefaultLiteral => Type.IsReferenceType ? "null" : "default";
    }

    private sealed class PermissionDef(string logicalName, string? displayName, string? description, Dictionary<string, ConstValue> props)
    {
        public string LogicalName { get; } = logicalName;
        public string? DisplayName { get; set; } = displayName;
        public string? Description { get; set; } = description;
        public Dictionary<string, ConstValue> Props { get; } = props;
    }

    private sealed class GroupDef(
        string logicalName,
        string? displayName,
        string? description,
        Dictionary<string, ConstValue> props,
        List<PermissionDef> permissions,
        List<GroupDef> children)
    {
        public string LogicalName { get; } = logicalName;
        public string? DisplayName { get; set; } = displayName;
        public string? Description { get; set; } = description;
        public Dictionary<string, ConstValue> Props { get; } = props;
        public List<PermissionDef> Permissions { get; } = permissions;
        public List<GroupDef> Children { get; } = children;
    }

    private sealed class Model(
        Compilation compilation,
        ITypeSymbol groupOptions,
        ITypeSymbol permissionOptions,
        ImmutableArray<GroupDef> groups,
        ImmutableArray<Diagnostic> diagnostics,
        bool hasFatal)
    {
        public Compilation Compilation { get; private set; } = compilation;
        public ITypeSymbol GroupOptions { get; private set; } = groupOptions;
        public ITypeSymbol PermissionOptions { get; private set; } = permissionOptions;
        public ImmutableArray<GroupDef> Groups { get; private set; } = groups;

        // ReSharper disable once MemberHidesStaticFromOuterClass
        public ImmutableArray<Diagnostic> Diagnostics { get; private set; } = diagnostics;
        public bool HasFatal { get; private set; } = hasFatal;
    }

    private static class SourceBuilders
    {
        public static string BuildModels(Model model)
        {
            var gProps = GetOptionProperties(model.GroupOptions);
            var pProps = GetOptionProperties(model.PermissionOptions);

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("namespace FluentPermissions.Core;");
            sb.AppendLine();
            // PermissionGroupInfo
            sb.AppendLine("public sealed class PermissionGroupInfo");
            sb.AppendLine("{");
            sb.AppendLine("    public string LogicalName { get; }");
            sb.AppendLine("    public string DisplayName { get; }");
            sb.AppendLine("    public string? Description { get; }");
            sb.AppendLine("    public string FullName { get; }");
            sb.AppendLine("    public string Key { get; }");
            sb.AppendLine("    public string? ParentKey { get; }");
            foreach (var prop in gProps)
            {
                sb.Append("    public ").Append(prop.TypeName).Append(' ').Append(prop.Name).AppendLine(" { get; }");
            }
            sb.AppendLine("    public System.Collections.Generic.IReadOnlyList<PermissionItemInfo> Permissions { get; }");
            sb.AppendLine("    public System.Collections.Generic.IReadOnlyList<PermissionGroupInfo> Children { get; }");
            // ctor
            sb.Append("    internal PermissionGroupInfo(string logicalName, string displayName, string? description, string fullName, string key, string? parentKey, System.Collections.Generic.IReadOnlyList<PermissionItemInfo> permissions, System.Collections.Generic.IReadOnlyList<PermissionGroupInfo> children");
            foreach (var prop in gProps)
            {
                sb.Append(", ").Append(prop.TypeName).Append(' ').Append(ToCamel(prop.Name));
            }

            sb.AppendLine(")");
            sb.AppendLine("    {");
            sb.AppendLine("        LogicalName = logicalName;");
            sb.AppendLine("        DisplayName = displayName;");
            sb.AppendLine("        Description = description;");
            sb.AppendLine("        FullName = fullName;");
            sb.AppendLine("        Key = key;");
            sb.AppendLine("        ParentKey = parentKey;");
            sb.AppendLine("        Permissions = permissions;");
            sb.AppendLine("        Children = children;");
            foreach (var prop in gProps)
            {
                sb.Append("        ").Append(prop.Name).Append(" = ").Append(ToCamel(prop.Name)).AppendLine(";");
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
            // PermissionItemInfo
            sb.AppendLine("public sealed class PermissionItemInfo");
            sb.AppendLine("{");
            sb.AppendLine("    public string LogicalName { get; }");
            sb.AppendLine("    public string DisplayName { get; }");
            sb.AppendLine("    public string? Description { get; }");
            sb.AppendLine("    public string FullName { get; }");
            sb.AppendLine("    public string Key { get; }");
            sb.AppendLine("    public string GroupKey { get; }");
            foreach (var prop in pProps)
            {
                sb.Append("    public ").Append(prop.TypeName).Append(' ').Append(prop.Name).AppendLine(" { get; }");
            }

            // implicit conversion
            sb.AppendLine(
                "    public static implicit operator string?(PermissionItemInfo permission) => permission?.Key;");
            // ctor
            sb.Append("    internal PermissionItemInfo(string logicalName, string displayName, string? description, string fullName, string key, string groupKey");
            foreach (var prop in pProps)
            {
                sb.Append(", ").Append(prop.TypeName).Append(' ').Append(ToCamel(prop.Name));
            }

            sb.AppendLine(")");
            sb.AppendLine("    {");
            sb.AppendLine("        LogicalName = logicalName;");
            sb.AppendLine("        DisplayName = displayName;");
            sb.AppendLine("        Description = description;");
            sb.AppendLine("        FullName = fullName;");
            sb.AppendLine("        Key = key;");
            sb.AppendLine("        GroupKey = groupKey;");
            foreach (var prop in pProps)
            {
                sb.Append("        ").Append(prop.Name).Append(" = ").Append(ToCamel(prop.Name)).AppendLine(";");
            }

            sb.AppendLine("    }");
            sb.AppendLine("    public override string ToString() => Key;");
            sb.AppendLine("}");

            return sb.ToString();
        }

        public static string BuildApp(Model model)
        {
            var gProps = GetOptionProperties(model.GroupOptions);
            var pProps = GetOptionProperties(model.PermissionOptions);

            var ns = GetRootNamespace(model.Compilation) + ".Generated";
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using FluentPermissions.Core;");
            sb.Append("namespace ").Append(ns).AppendLine(";");
            sb.AppendLine();
            sb.AppendLine("public static partial class AppPermissions");
            sb.AppendLine("{");

            var keys = new List<(string ConstName, string KeyValue)>();

            // Recursive declare fields
            foreach (var root in model.Groups)
            {
                DeclareGroupAndChildren(sb, root, [], gProps, pProps, keys);
            }

            // Static ctor: build GroupsByKey
            sb.AppendLine();
            sb.AppendLine("    public static readonly global::System.Collections.Generic.IReadOnlyDictionary<string, PermissionGroupInfo> GroupsByKey;");
            sb.AppendLine();
            sb.AppendLine("    static AppPermissions()");
            sb.AppendLine("    {");
            sb.AppendLine("        var dict = new global::System.Collections.Generic.Dictionary<string, PermissionGroupInfo>(global::System.StringComparer.Ordinal);");
            foreach (var root in model.Groups)
            {
                AddGroupToDict(sb, root, []);
            }
            sb.AppendLine("        GroupsByKey = dict;");
            sb.AppendLine("    }");

            // Nested classes per group
            sb.AppendLine();
            foreach (var root in model.Groups)
            {
                BuildNestedAccessors(sb, root, []);
            }

            sb.AppendLine();
            // Access helpers
            sb.AppendLine(
                "    public static global::System.Collections.Generic.IReadOnlyList<PermissionGroupInfo> GetAllGroups() => new PermissionGroupInfo[] { ");
            sb.Append("        ")
                .Append(string.Join(", ", model.Groups.Select(g => FieldNameForGroup([], g.LogicalName))))
                .AppendLine();
            sb.AppendLine("    };");

            // Flat Keys class
            sb.AppendLine();
            sb.AppendLine("    public static class Keys");
            sb.AppendLine("    {");
            foreach (var (constName, keyValue) in keys.OrderBy(k => k.ConstName, StringComparer.Ordinal))
            {
                sb.Append("        public const string ").Append(constName).Append(" = ").Append(EscapeString(keyValue))
                    .AppendLine(";");
            }

            sb.AppendLine("    }");

            sb.AppendLine("}");

            return sb.ToString();
        }

        private static void DeclareGroupAndChildren(StringBuilder sb, GroupDef group, List<string> path,
            List<OptionProperty> gProps, List<OptionProperty> pProps, List<(string ConstName, string KeyValue)> keys)
        {
            var newPath = new List<string>(path) { group.LogicalName };
            var dotted = string.Join(".", newPath);
            var fullName = dotted.Replace('.', '_');

            // First declare child groups (bottom-up requirement)
            foreach (var child in group.Children)
            {
                DeclareGroupAndChildren(sb, child, newPath, gProps, pProps, keys);
            }

            // Permissions fields (declare items)
            foreach (var perm in group.Permissions)
            {
                var permDotted = dotted + "." + perm.LogicalName;
                var permFull = permDotted.Replace('.', '_');
                var fieldName = FieldNameForPermission(newPath, perm.LogicalName);
                sb.Append("    private static readonly PermissionItemInfo ")
                    .Append(fieldName)
                    .Append(" = new PermissionItemInfo(")
                    .Append(EscapeString(perm.LogicalName)).Append(", ")
                    .Append(EscapeString(perm.DisplayName ?? perm.LogicalName)).Append(", ")
                    .Append(EscapeString(perm.Description)).Append(", ")
                    .Append(EscapeString(permFull)).Append(", ")
                    .Append(EscapeString(permDotted)).Append(", ")
                    .Append(EscapeString(dotted));
                foreach (var pp in pProps)
                {
                    perm.Props.TryGetValue(pp.Name, out var val);
                    sb.Append(", ").Append(val?.ToCSharpLiteral() ?? pp.DefaultLiteral);
                }

                sb.AppendLine(");");
                // Keys constants map to dotted value
                keys.Add((permFull, permDotted));
            }

            // Build arrays for permissions and children to pass to group constructor
            var permArray = group.Permissions.Count > 0
                ? "new PermissionItemInfo[] { " + string.Join(", ", group.Permissions.Select(p => FieldNameForPermission(newPath, p.LogicalName))) + " }"
                : "global::System.Array.Empty<PermissionItemInfo>()";

            var childArray = group.Children.Count > 0
                ? "new PermissionGroupInfo[] { " + string.Join(", ", group.Children.Select(ch => FieldNameForGroup(newPath, ch.LogicalName))) + " }"
                : "global::System.Array.Empty<PermissionGroupInfo>()";

            // Group field
            sb.Append("    private static readonly PermissionGroupInfo ").Append(FieldNameForGroup(path, group.LogicalName))
                .Append(" = new PermissionGroupInfo(")
                .Append(EscapeString(group.LogicalName)).Append(", ")
                .Append(EscapeString(group.DisplayName ?? group.LogicalName)).Append(", ")
                .Append(EscapeString(group.Description)).Append(", ")
                .Append(EscapeString(fullName)).Append(", ")
                .Append(EscapeString(dotted)).Append(", ")
                .Append(path.Count == 0 ? "null" : EscapeString(string.Join(".", path))).Append(", ")
                .Append(permArray).Append(", ")
                .Append(childArray);
            foreach (var gp in gProps)
            {
                group.Props.TryGetValue(gp.Name, out var val);
                sb.Append(", ").Append(val?.ToCSharpLiteral() ?? gp.DefaultLiteral);
            }
            sb.AppendLine(");");
        }

        private static void BuildNestedAccessors(StringBuilder sb, GroupDef group, List<string> path)
        {
            var newPath = new List<string>(path) { group.LogicalName };
            sb.Append("    public static class ").Append(SafeIdent(group.LogicalName)).AppendLine();
            sb.AppendLine("    {");
            sb.Append("        public static readonly PermissionGroupInfo Group = ")
                .Append(FieldNameForGroup(path, group.LogicalName)).AppendLine(";");

            foreach (var p in group.Permissions)
            {
                sb.Append("        public static readonly PermissionItemInfo ").Append(SafeIdent(p.LogicalName)).Append(" = ")
                    .Append(FieldNameForPermission(newPath, p.LogicalName)).AppendLine(";");
            }

            foreach (var child in group.Children)
            {
                BuildNestedAccessors(sb, child, newPath);
            }

            sb.AppendLine("    }");
        }

        private static string FieldNameForGroup(List<string> path, string name)
        {
            var full = string.Join("_", path.Append(name).Select(SafeIdent));
            return full + "Group";
        }

        private static string FieldNameForPermission(List<string> path, string name)
        {
            var full = string.Join("_", path.Append(name).Select(SafeIdent));
            return full;
        }

        private static List<OptionProperty> GetOptionProperties(ITypeSymbol options)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<OptionProperty>();
            for (var t = options; t is not null; t = t.BaseType)
            {
                foreach (var p in t.GetMembers().OfType<IPropertySymbol>())
                {
                    if (p.DeclaredAccessibility != Accessibility.Public) continue;
                    if (p.SetMethod is null || p.SetMethod.DeclaredAccessibility != Accessibility.Public) continue;
                    if (!seen.Add(p.Name)) continue; // prefer most derived
                    if (!string.Equals(p.Name, "Description", StringComparison.Ordinal))
                        result.Add(new OptionProperty(p.Name, p.Type));
                }
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return result;
        }

        private static string GetRootNamespace(Compilation compilation)
        {
            // Prefer AssemblyName; RootNamespace is not available via Compilation directly
            return compilation.AssemblyName ?? "App";
        }

        private static string ToCamel(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (name.Length == 1) return name.ToLowerInvariant();
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        private static string SafeIdent(string name)
        {
            var sb = new StringBuilder(name.Length);
            if (name.Length == 0 || !SyntaxFacts.IsIdentifierStartCharacter(name[0])) sb.Append('_');
            foreach (var ch in name)
            {
                sb.Append(SyntaxFacts.IsIdentifierPartCharacter(ch) ? ch : '_');
            }

            return sb.ToString();
        }

        private static void AddGroupToDict(StringBuilder sb, GroupDef group, List<string> path)
        {
            var newPath = new List<string>(path) { group.LogicalName };
            var dotted = string.Join(".", newPath);
            sb.Append("        dict[").Append(EscapeString(dotted)).Append("] = ").Append(FieldNameForGroup(path, group.LogicalName)).AppendLine(";");
            foreach (var child in group.Children)
            {
                AddGroupToDict(sb, child, newPath);
            }
        }
    }
}