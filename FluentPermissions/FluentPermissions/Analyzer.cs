using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FluentPermissions;

internal sealed class Analyzer(Compilation compilation)
{
    public Model Analyze(ImmutableArray<RegistrarInfo> registrars)
    {
        var diags = ImmutableArray.CreateBuilder<Diagnostic>();

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
                    return new Model(compilation, ImmutableArray<GroupDef>.Empty,
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

            foreach (var decl in registerMethod.DeclaringSyntaxReferences)
            {
                if (decl.GetSyntax() is not MethodDeclarationSyntax mds) continue;
                var groups = ParseRegisterBody(mds);
                allGroups.AddRange(groups);
            }
        }

        return new Model(compilation, allGroups.ToImmutableArray(),
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
            if (!calls.Any(c => c.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "DefineGroup" }))
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

                        if (semanticModel.GetSymbolInfo(call).Symbol is IMethodSymbol methodSymbol)
                        {
                            var builderLambdaArg = GetBuilderLambdaArgumentIfAny(methodSymbol, call);
                            if (builderLambdaArg is not null)
                            {
                                ProcessBuilderLambda(grp, builderLambdaArg, semanticModel);
                                if (stack.Count > 0) stack.Pop();
                            }
                        }
                        break;
                    }
                    case "WithOptions" when stack.Count == 0:
                        break;
                    case "WithOptions":
                    {
                        var args = call.ArgumentList.Arguments;
                        if (args.Count > 0)
                        {
                            var cur = stack.Peek();
                            ExtractAssignmentsFromLambda(semanticModel, args[0].Expression, cur.Props);
                        }
                        break;
                    }
                    case "AddPermission" when stack.Count == 0:
                        continue;
                    case "AddPermission":
                    {
                        var args = call.ArgumentList.Arguments;
                        if (args.Count == 0) continue;
                        var parsed = ParseGroupOrPermissionArguments(semanticModel, args);
                        if (parsed.LogicalName is null) continue;
                        AddOrUpdatePermission(stack.Peek(), parsed.LogicalName, parsed.DisplayName, parsed.Description, parsed.Props);
                        break;
                    }
                }
            }
        }

        foreach (var g in groupsRoot)
            yield return g;
        yield break;

        GroupDef GetOrAddGroup(GroupDef? parent, string name, Dictionary<string, ConstValue> props)
        {
            if (parent is null)
            {
                var existing =
                    groupsRoot.FirstOrDefault(g => string.Equals(g.LogicalName, name, StringComparison.Ordinal));
                if (existing is not null) return existing;
                var created = new GroupDef(name, null, null, props, new List<PermissionDef>(), new List<GroupDef>());
                groupsRoot.Add(created);
                return created;
            }
            else
            {
                var existing =
                    parent.Children.FirstOrDefault(g => string.Equals(g.LogicalName, name, StringComparison.Ordinal));
                if (existing is not null) return existing;
                var created = new GroupDef(name, null, null, props, new List<PermissionDef>(), new List<GroupDef>());
                parent.Children.Add(created);
                return created;
            }
        }
    }

    private static ArgumentSyntax? GetBuilderLambdaArgumentIfAny(IMethodSymbol methodSymbol, InvocationExpressionSyntax call)
    {
        var paramIndex = -1;
        for (var i = 0; i < methodSymbol.Parameters.Length; i++)
        {
            var p = methodSymbol.Parameters[i].Type as INamedTypeSymbol;
            if (p is null) continue;
            if (!string.Equals(p.Name, "Action", StringComparison.Ordinal)) continue;
            if (p.TypeArguments.Length != 1) continue;
            var targ = p.TypeArguments[0];
            var targName = targ.ToDisplayString();
            if (targName.IndexOf("PermissionGroupBuilder", StringComparison.Ordinal) < 0) continue;
            paramIndex = i;
            break;
        }

        if (paramIndex < 0) return null;
        var args = call.ArgumentList.Arguments;
        return paramIndex >= args.Count ? null : args[paramIndex];
    }

    private void ProcessBuilderLambda(GroupDef current, ArgumentSyntax lambdaArg, SemanticModel semanticModel)
    {
        var expr = lambdaArg.Expression;
        var body = expr switch
        {
            // ReSharper disable once IdentifierTypo
            ParenthesizedLambdaExpressionSyntax ples => ples.Body,
            SimpleLambdaExpressionSyntax sles => sles.Body,
            _ => null
        };
        if (body is null) return;

        var stack = new Stack<GroupDef>();
        stack.Push(current);

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

        return;

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

                    if (semanticModel.GetSymbolInfo(call).Symbol is IMethodSymbol methodSymbol)
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

        for (var i = 1; i < args.Count; i++)
        {
            var expr = args[i].Expression;
            var str = GetConstString(semanticModel, expr);
            if (str is not null)
            {
                if (displayName is null) { displayName = str; continue; }
                if (description is null) { description = str; continue; }
            }
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

        return stack;
    }

    private static void ExtractAssignmentsFromLambda(SemanticModel semanticModel, ExpressionSyntax expr,
        Dictionary<string, ConstValue> into)
    {
        switch (expr)
        {
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
}
