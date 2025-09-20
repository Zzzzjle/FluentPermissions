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
                static (ctx, ct) => TryGetRegistrar(ctx)).Where(static r => r is not null)!
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
        var symbol = model.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        if (symbol is null) return null;

        var registrarInterface = symbol.AllInterfaces.FirstOrDefault(i => i.OriginalDefinition.ToDisplayString() ==
            "FluentPermissions.Core.Abstractions.IPermissionRegistrar<TGroupOptions, TPermissionOptions>");
        if (registrarInterface is null) return null;

        return new RegistrarInfo(symbol, registrarInterface);
    }

    private sealed class Analyzer
    {
        private readonly Compilation _compilation;
        public Analyzer(Compilation compilation)
        {
            _compilation = compilation;
        }

        public Model Analyze(ImmutableArray<RegistrarInfo> registrars)
        {
            var diags = ImmutableArray.CreateBuilder<Diagnostic>();

            // Ensure all registrars use consistent option types
            ITypeSymbol? groupOptions = null;
            ITypeSymbol? permOptions = null;

            var allGroups = new List<GroupDef>();

            foreach (var reg in registrars)
            {
                var iface = reg.Interface;
                var g = iface.TypeArguments[0];
                var p = iface.TypeArguments[1];
                if (groupOptions is null) { groupOptions = g; permOptions = p; }
                else
                {
                    if (!SymbolEqualityComparer.Default.Equals(groupOptions, g) ||
                        !SymbolEqualityComparer.Default.Equals(permOptions!, p))
                    {
                        diags.Add(Diagnostic.Create(Diagnostics.InconsistentOptionsTypes, reg.Symbol.Locations.FirstOrDefault()));
                        return new Model(_compilation, groupOptions!, permOptions!, ImmutableArray<GroupDef>.Empty, diags.ToImmutable(), hasFatal: true);
                    }
                }

                var registerMethod = reg.Symbol.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(m => m.Name == "Register" && m.Parameters.Length == 1);
                if (registerMethod is null)
                {
                    diags.Add(Diagnostic.Create(Diagnostics.MissingRegisterMethod, reg.Symbol.Locations.FirstOrDefault()));
                    continue;
                }

                // Try to find the method syntax
                foreach (var decl in registerMethod.DeclaringSyntaxReferences)
                {
                    if (decl.GetSyntax() is MethodDeclarationSyntax mds)
                    {
                        var groups = ParseRegisterBody(mds);
                        allGroups.AddRange(groups);
                    }
                }
            }

            return new Model(_compilation, groupOptions!, permOptions!, allGroups.ToImmutableArray(), diags.ToImmutable(), hasFatal: false);
        }

        private IEnumerable<GroupDef> ParseRegisterBody(MethodDeclarationSyntax methodSyntax)
        {
            var semanticModel = _compilation.GetSemanticModel(methodSyntax.SyntaxTree);
            if (methodSyntax.Body is null && methodSyntax.ExpressionBody is null)
                yield break;

            var root = (SyntaxNode)(methodSyntax.Body ?? (SyntaxNode)methodSyntax.ExpressionBody!);
            var groupsRoot = new List<GroupDef>();
            // Helper for de-duplication
            GroupDef GetOrAddGroup(GroupDef? parent, string name, Dictionary<string, ConstValue> props)
            {
                if (parent is null)
                {
                    var existing = groupsRoot.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.Ordinal));
                    if (existing is not null) return existing;
                    var created = new GroupDef(name, props, new List<PermissionDef>(), new List<GroupDef>());
                    groupsRoot.Add(created);
                    return created;
                }
                else
                {
                    var existing = parent.Children.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.Ordinal));
                    if (existing is not null) return existing;
                    var created = new GroupDef(name, props, new List<PermissionDef>(), new List<GroupDef>());
                    parent.Children.Add(created);
                    return created;
                }
            }
            void AddPermissionIfMissing(GroupDef parent, string name, Dictionary<string, ConstValue> props)
            {
                if (parent.Permissions.Any(p => string.Equals(p.Name, name, StringComparison.Ordinal))) return;
                parent.Permissions.Add(new PermissionDef(name, props));
            }

            foreach (var stmt in root.DescendantNodes().OfType<ExpressionStatementSyntax>())
            {
                if (stmt.Expression is not InvocationExpressionSyntax invRoot) continue;
                var calls = FlattenCalls(invRoot).ToList();
                if (!calls.Any(c => c.Expression is MemberAccessExpressionSyntax maesX && maesX.Name.Identifier.Text == "DefineGroup"))
                    continue;

                var stack = new Stack<GroupDef>();

                foreach (var call in calls)
                {
                    if (call.Expression is not MemberAccessExpressionSyntax maes) continue;
                    var methodName = maes.Name.Identifier.Text;
                    if (methodName == "DefineGroup")
                    {
                        var args = call.ArgumentList.Arguments;
                        if (args.Count == 0) continue;
                        var groupName = GetConstString(semanticModel, args[0].Expression);
                        if (groupName is null) continue;
                        var groupProps = new Dictionary<string, ConstValue>(StringComparer.Ordinal);
                        if (args.Count >= 2)
                        {
                            ExtractAssignmentsFromLambda(semanticModel, args[1].Expression, groupProps);
                        }
                        var parent = stack.Count == 0 ? null : stack.Peek();
                        var grp = GetOrAddGroup(parent, groupName, groupProps);
                        stack.Push(grp);
                    }
                    else if (methodName == "AddPermission")
                    {
                        if (stack.Count == 0) continue; // skip if no current group
                        var args = call.ArgumentList.Arguments;
                        if (args.Count == 0) continue;
                        var permName = GetConstString(semanticModel, args[0].Expression);
                        if (permName is null) continue;
                        var permProps = new Dictionary<string, ConstValue>(StringComparer.Ordinal);
                        if (args.Count >= 2)
                        {
                            ExtractAssignmentsFromLambda(semanticModel, args[1].Expression, permProps);
                        }
                        AddPermissionIfMissing(stack.Peek(), permName, permProps);
                    }
                    else if (methodName == "Then")
                    {
                        if (stack.Count > 0) stack.Pop();
                    }
                }
            }

            foreach (var g in groupsRoot)
                yield return g;
        }

        private static IEnumerable<InvocationExpressionSyntax> FlattenCalls(InvocationExpressionSyntax root)
        {
            var stack = new Stack<InvocationExpressionSyntax>();
            ExpressionSyntax? cur = root;
            while (cur is InvocationExpressionSyntax inv && inv.Expression is MemberAccessExpressionSyntax maes)
            {
                stack.Push(inv);
                cur = maes.Expression;
            }
            return stack; // Stack enumerates from last pushed to first, which in our loop yields earliest-to-latest calls
        }

        private static InvocationExpressionSyntax GetOutermostInvocation(InvocationExpressionSyntax node)
        {
            SyntaxNode cur = node;
            while (cur.Parent is InvocationExpressionSyntax parent && parent.Expression is MemberAccessExpressionSyntax)
            {
                cur = parent;
            }
            return (InvocationExpressionSyntax)cur;
        }

        private static void ExtractAssignmentsFromLambda(SemanticModel semanticModel, ExpressionSyntax expr, Dictionary<string, ConstValue> into)
        {
            // Expect a simple lambda like: options => { options.Prop = <literal>; ... }
            // or: o => o.Prop = 123
            if (expr is ParenthesizedLambdaExpressionSyntax ples)
            {
                var paramName = ples.ParameterList.Parameters.FirstOrDefault()?.Identifier.Text;
                if (paramName is null) return;
                ExtractAssignmentsFromLambdaBody(semanticModel, paramName, ples.Body, into);
            }
            else if (expr is SimpleLambdaExpressionSyntax sles)
            {
                var paramName = sles.Parameter.Identifier.Text;
                ExtractAssignmentsFromLambdaBody(semanticModel, paramName, sles.Body, into);
            }
        }

        private static void ExtractAssignmentsFromLambdaBody(SemanticModel semanticModel, string paramName, CSharpSyntaxNode body, Dictionary<string, ConstValue> into)
        {
            if (body is BlockSyntax block)
            {
                foreach (var stmt in block.Statements.OfType<ExpressionStatementSyntax>())
                {
                    if (stmt.Expression is AssignmentExpressionSyntax assign)
                    {
                        TryCaptureAssignment(semanticModel, paramName, assign, into);
                    }
                }
            }
            else if (body is ExpressionSyntax expr)
            {
                if (expr is AssignmentExpressionSyntax assign)
                {
                    TryCaptureAssignment(semanticModel, paramName, assign, into);
                }
            }
        }

        private static void TryCaptureAssignment(SemanticModel semanticModel, string paramName, AssignmentExpressionSyntax assign, Dictionary<string, ConstValue> into)
        {
            if (assign.Left is MemberAccessExpressionSyntax maes && maes.Expression is IdentifierNameSyntax id && id.Identifier.Text == paramName)
            {
                var propName = maes.Name.Identifier.Text;
                var value = GetConstValue(semanticModel, assign.Right);
                if (value is not null)
                {
                    into[propName] = value;
                }
            }
        }

        private static string? GetConstString(SemanticModel semanticModel, ExpressionSyntax expr)
        {
            var cv = GetConstValue(semanticModel, expr);
            return cv?.Kind == ConstKind.String && cv.Value is string s ? s : null;
        }

        private static ConstValue? GetConstValue(SemanticModel semanticModel, ExpressionSyntax expr)
        {
            expr = (expr as InvocationExpressionSyntax)?.Expression as ExpressionSyntax ?? expr;
            var c = semanticModel.GetConstantValue(expr);
            if (c.HasValue)
            {
                if (c.Value is string s) return new ConstValue(ConstKind.String, s);
                if (c.Value is bool b) return new ConstValue(ConstKind.Bool, b);
                if (c.Value is int i) return new ConstValue(ConstKind.Int, i);
                if (c.Value is double d) return new ConstValue(ConstKind.Double, d);
            }
            if (expr.IsKind(SyntaxKind.NullLiteralExpression)) return new ConstValue(ConstKind.Null, null);
            return null;
        }
    }

    private static class Diagnostics
    {
        public static readonly DiagnosticDescriptor InconsistentOptionsTypes = new(
            id: "FP001",
            title: "Inconsistent option types across registrars",
            messageFormat: "All IPermissionRegistrar implementations must use the same TGroupOptions and TPermissionOptions types within one project",
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

    private sealed class RegistrarInfo
    {
        public INamedTypeSymbol Symbol { get; }
        public INamedTypeSymbol Interface { get; }
        public RegistrarInfo(INamedTypeSymbol symbol, INamedTypeSymbol @interface)
        {
            Symbol = symbol;
            Interface = @interface;
        }
    }

    private enum ConstKind { String, Bool, Int, Double, Null }
    private sealed class ConstValue
    {
        public ConstKind Kind { get; }
        public object? Value { get; }
        public ConstValue(ConstKind kind, object? value)
        {
            Kind = kind;
            Value = value;
        }
        public string ToCSharpLiteral()
        {
            switch (Kind)
            {
                case ConstKind.String: return EscapeString((string?)Value);
                case ConstKind.Bool: return (bool)Value! ? "true" : "false";
                case ConstKind.Int: return ((int)Value!).ToString(System.Globalization.CultureInfo.InvariantCulture);
                case ConstKind.Double: return ((double)Value!).ToString(System.Globalization.CultureInfo.InvariantCulture);
                case ConstKind.Null: return "null";
                default: return "default";
            }
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

    private sealed class OptionProperty
    {
        public string Name { get; }
        public ITypeSymbol Type { get; }
        public OptionProperty(string name, ITypeSymbol type)
        {
            Name = name;
            Type = type;
        }
        public string TypeName { get { return Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat); } }
        public string DefaultLiteral { get { return Type.IsReferenceType ? "null" : "default"; } }
    }

    private sealed class PermissionDef
    {
        public string Name { get; }
        public Dictionary<string, ConstValue> Props { get; }
        public PermissionDef(string name, Dictionary<string, ConstValue> props)
        {
            Name = name;
            Props = props;
        }
    }

    private sealed class GroupDef
    {
        public string Name { get; }
        public Dictionary<string, ConstValue> Props { get; }
        public List<PermissionDef> Permissions { get; }
        public List<GroupDef> Children { get; }
        public GroupDef(string name, Dictionary<string, ConstValue> props, List<PermissionDef> permissions, List<GroupDef> children)
        {
            Name = name;
            Props = props;
            Permissions = permissions;
            Children = children;
        }
    }

    private sealed class Model
    {
        public Compilation Compilation { get; private set; }
        public ITypeSymbol GroupOptions { get; private set; }
        public ITypeSymbol PermissionOptions { get; private set; }
        public ImmutableArray<GroupDef> Groups { get; private set; }
        public ImmutableArray<Diagnostic> Diagnostics { get; private set; }
        public bool HasFatal { get; private set; }

        public Model(Compilation compilation, ITypeSymbol groupOptions, ITypeSymbol permissionOptions, ImmutableArray<GroupDef> groups, ImmutableArray<Diagnostic> diagnostics, bool hasFatal)
        {
            Compilation = compilation;
            GroupOptions = groupOptions;
            PermissionOptions = permissionOptions;
            Groups = groups;
            Diagnostics = diagnostics;
            HasFatal = hasFatal;
        }
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
            sb.AppendLine("    public string Name { get; }");
            sb.AppendLine("    public string FullName { get; }");
            sb.AppendLine("    public string Key { get; }");
            foreach (var prop in gProps)
            {
                sb.Append("    public ").Append(prop.TypeName).Append(' ').Append(prop.Name).AppendLine(" { get; }");
            }
            sb.AppendLine("    public System.Collections.Generic.IReadOnlyList<PermissionItemInfo> Permissions { get; internal set; }");
            sb.AppendLine("    public System.Collections.Generic.IReadOnlyList<PermissionGroupInfo> Children { get; internal set; }");
            // ctor
            sb.Append("    internal PermissionGroupInfo(string name, string fullName, string key");
            foreach (var prop in gProps)
            {
                sb.Append(", ").Append(prop.TypeName).Append(' ').Append(ToCamel(prop.Name));
            }
            sb.AppendLine(")");
            sb.AppendLine("    {");
            sb.AppendLine("        Name = name;");
            sb.AppendLine("        FullName = fullName;");
            sb.AppendLine("        Key = key;");
            foreach (var prop in gProps)
            {
                sb.Append("        ").Append(prop.Name).Append(" = ").Append(ToCamel(prop.Name)).AppendLine(";");
            }
            sb.AppendLine("        Permissions = System.Array.Empty<PermissionItemInfo>();");
            sb.AppendLine("        Children = System.Array.Empty<PermissionGroupInfo>();");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
            // PermissionItemInfo
            sb.AppendLine("public sealed class PermissionItemInfo");
            sb.AppendLine("{");
            sb.AppendLine("    public string Name { get; }");
            sb.AppendLine("    public string GroupName { get; }");
            sb.AppendLine("    public string FullName { get; }");
            sb.AppendLine("    public string Key { get; }");
            sb.AppendLine("    public PermissionGroupInfo? Group { get; internal set; }");
            foreach (var prop in pProps)
            {
                sb.Append("    public ").Append(prop.TypeName).Append(' ').Append(prop.Name).AppendLine(" { get; }");
            }
            // implicit conversion
            sb.AppendLine("    public static implicit operator string?(PermissionItemInfo permission) => permission?.Name;");
            // ctor
            sb.Append("    internal PermissionItemInfo(string name, string groupName, string fullName, string key");
            foreach (var prop in pProps)
            {
                sb.Append(", ").Append(prop.TypeName).Append(' ').Append(ToCamel(prop.Name));
            }
            sb.AppendLine(")");
            sb.AppendLine("    {");
            sb.AppendLine("        Name = name;");
            sb.AppendLine("        GroupName = groupName;");
            sb.AppendLine("        FullName = fullName;");
            sb.AppendLine("        Key = key;");
            foreach (var prop in pProps)
            {
                sb.Append("        ").Append(prop.Name).Append(" = ").Append(ToCamel(prop.Name)).AppendLine(";");
            }
            sb.AppendLine("    }");
            sb.AppendLine("    public override string ToString() => Name;");
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
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using FluentPermissions.Core;");
            sb.Append("namespace ").Append(ns).AppendLine(";");
            sb.AppendLine();
            sb.AppendLine("public static partial class AppPermissions");
            sb.AppendLine("{");

            var keys = new List<(string ConstName, string KeyValue)>();

            // Recursive declare fields
            foreach (var root in model.Groups)
            {
                DeclareGroupAndChildren(sb, root, new List<string>(), gProps, pProps, keys);
            }

            // Static ctor: link graph
            sb.AppendLine();
            sb.AppendLine("    static AppPermissions()");
            sb.AppendLine("    {");
            foreach (var root in model.Groups)
            {
                LinkGroup(sb, root, new List<string>());
            }
            sb.AppendLine("    }");

            // Nested classes per group
            sb.AppendLine();
            foreach (var root in model.Groups)
            {
                BuildNestedAccessors(sb, root, new List<string>());
            }

            sb.AppendLine();
            // Access helpers
            sb.AppendLine("    public static global::System.Collections.Generic.IReadOnlyList<PermissionGroupInfo> GetAllGroups() => new PermissionGroupInfo[] {");
            sb.Append("        ").Append(string.Join(", ", model.Groups.Select(g => FieldNameForGroup(new List<string>(), g.Name)))).AppendLine();
            sb.AppendLine("    };");

            // Flat Keys class
            sb.AppendLine();
            sb.AppendLine("    public static class Keys");
            sb.AppendLine("    {");
            foreach (var (constName, keyValue) in keys.OrderBy(k => k.ConstName, System.StringComparer.Ordinal))
            {
                sb.Append("        public const string ").Append(constName).Append(" = ").Append(EscapeString(keyValue)).AppendLine(";");
            }
            sb.AppendLine("    }");

            sb.AppendLine("}");

            return sb.ToString();
        }

        private static void DeclareGroupAndChildren(StringBuilder sb, GroupDef group, List<string> path, List<OptionProperty> gProps, List<OptionProperty> pProps, List<(string ConstName, string KeyValue)> keys)
        {
            var newPath = new List<string>(path) { group.Name };
            var dotted = string.Join(".", newPath);
            var fullName = dotted.Replace('.', '_');
            var key = ComputeSha256Hex(dotted);

            // Group field
            sb.Append("    public static readonly PermissionGroupInfo ").Append(FieldNameForGroup(path, group.Name))
                .Append(" = new PermissionGroupInfo(")
                .Append(EscapeString(group.Name)).Append(", ")
                .Append(EscapeString(fullName)).Append(", ")
                .Append(EscapeString(key));
            foreach (var gp in gProps)
            {
                group.Props.TryGetValue(gp.Name, out var val);
                sb.Append(", ").Append(val?.ToCSharpLiteral() ?? gp.DefaultLiteral);
            }
            sb.AppendLine(");");

            // Permissions fields
            foreach (var perm in group.Permissions)
            {
                var permDotted = dotted + "." + perm.Name;
                var permFull = permDotted.Replace('.', '_');
                var permKey = ComputeSha256Hex(permDotted);
                var fieldName = FieldNameForPermission(newPath, perm.Name);
                sb.Append("    public static readonly PermissionItemInfo ")
                    .Append(fieldName)
                    .Append(" = new PermissionItemInfo(")
                    .Append(EscapeString(perm.Name)).Append(", ")
                    .Append(EscapeString(dotted)).Append(", ")
                    .Append(EscapeString(permFull)).Append(", ")
                    .Append(EscapeString(permKey));
                foreach (var pp in pProps)
                {
                    perm.Props.TryGetValue(pp.Name, out var val);
                    sb.Append(", ").Append(val?.ToCSharpLiteral() ?? pp.DefaultLiteral);
                }
                sb.AppendLine(");");
                // Collect Keys constants
                keys.Add((permFull, permKey));
            }

            // Children groups recursively
            foreach (var child in group.Children)
            {
                DeclareGroupAndChildren(sb, child, newPath, gProps, pProps, keys);
            }
        }

        private static void LinkGroup(StringBuilder sb, GroupDef group, List<string> path)
        {
            var fieldName = FieldNameForGroup(path, group.Name);
            var newPath = new List<string>(path) { group.Name };

            // Link permissions
            if (group.Permissions.Count > 0)
            {
                var items = group.Permissions.Select(p => FieldNameForPermission(newPath, p.Name));
                sb.Append("        ").Append(fieldName).Append(".Permissions = new PermissionItemInfo[] { ")
                    .Append(string.Join(", ", items)).AppendLine(" };");
                foreach (var p in group.Permissions)
                {
                    sb.Append("        ").Append(FieldNameForPermission(newPath, p.Name)).Append(".Group = ")
                        .Append(fieldName).AppendLine(";");
                }
            }

            // Link children groups
            if (group.Children.Count > 0)
            {
                var childrenFields = group.Children.Select(ch => FieldNameForGroup(newPath, ch.Name));
                sb.Append("        ").Append(fieldName).Append(".Children = new PermissionGroupInfo[] { ")
                    .Append(string.Join(", ", childrenFields)).AppendLine(" };");
            }

            foreach (var child in group.Children)
                LinkGroup(sb, child, newPath);
        }

        private static void BuildNestedAccessors(StringBuilder sb, GroupDef group, List<string> path)
        {
            var newPath = new List<string>(path) { group.Name };
            sb.Append("    public static class ").Append(SafeIdent(group.Name)).AppendLine();
            sb.AppendLine("    {");
            sb.Append("        public static readonly PermissionGroupInfo Group = ").Append(FieldNameForGroup(path, group.Name)).AppendLine(";");

            // Names (dotted) constants for permissions
            if (group.Permissions.Count > 0)
            {
                sb.AppendLine("        public static class Names");
                sb.AppendLine("        {");
                var dottedBase = string.Join(".", newPath);
                foreach (var p in group.Permissions)
                {
                    var dotted = dottedBase + "." + p.Name;
                    sb.Append("            public const string ").Append(SafeIdent(p.Name)).Append(" = ")
                        .Append(EscapeString(dotted)).AppendLine(";");
                }
                sb.AppendLine("        }");
            }

            foreach (var p in group.Permissions)
            {
                sb.Append("        public static readonly PermissionItemInfo ").Append(SafeIdent(p.Name)).Append(" = ")
                    .Append(FieldNameForPermission(newPath, p.Name)).AppendLine(";");
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

        private static string ComputeSha256Hex(string input)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(input);
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
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
                    result.Add(new OptionProperty(p.Name, p.Type));
                }
            }
            result.Sort((a,b) => string.CompareOrdinal(a.Name, b.Name));
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
    }
}
