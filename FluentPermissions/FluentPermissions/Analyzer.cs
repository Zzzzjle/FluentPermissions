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
        var sawGeneric = false;

        var allGroups = new List<GroupDef>();

        foreach (var reg in registrars)
        {
            // Check generic consistency
            if (!reg.IsNonGeneric)
            {
                sawGeneric = true;
                var @interface = reg.Interface!;
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
                            diags.ToImmutable(), true);
                    }
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

        var groupProps = sawGeneric
            ? CollectOptionProps(groupOptions as INamedTypeSymbol)
            : ImmutableArray<OptionProp>.Empty;
        var permProps = sawGeneric
            ? CollectOptionProps(permOptions as INamedTypeSymbol)
            : ImmutableArray<OptionProp>.Empty;

        return new Model(compilation, allGroups.ToImmutableArray(),
            diags.ToImmutable(), false, groupProps, permProps);
    }

    private static ImmutableArray<OptionProp> CollectOptionProps(INamedTypeSymbol? optionType)
    {
        if (optionType is null) return ImmutableArray<OptionProp>.Empty;
        var list = new List<OptionProp>();
        foreach (var m in optionType.GetMembers().OfType<IPropertySymbol>())
        {
            if (m.IsStatic) continue;
            if (m.DeclaredAccessibility != Accessibility.Public) continue;
            // Only consider simple primitive-like types we can embed as literals
            var st = m.Type.SpecialType;
            ConstKind? kind = st switch
            {
                SpecialType.System_Boolean => ConstKind.Bool,
                SpecialType.System_Int32 => ConstKind.Int,
                SpecialType.System_Double => ConstKind.Double,
                SpecialType.System_String => ConstKind.String,
                _ => null
            };
            if (kind is null) continue;
            list.Add(new OptionProp(m.Name, kind.Value));
        }

        return list.ToImmutableArray();
    }

    private IEnumerable<GroupDef> ParseRegisterBody(MethodDeclarationSyntax methodSyntax)
    {
        var semanticModel = compilation.GetSemanticModel(methodSyntax.SyntaxTree);
        if (methodSyntax.Body is null && methodSyntax.ExpressionBody is null)
            yield break;

        var groupsRoot = new List<GroupDef>();

        var topLevelInvocations = Enumerable.Empty<InvocationExpressionSyntax>();
        if (methodSyntax.Body is { } block)
            topLevelInvocations = block.Statements
                .OfType<ExpressionStatementSyntax>()
                .Select(s => s.Expression)
                .OfType<InvocationExpressionSyntax>();
        else if (methodSyntax.ExpressionBody is { Expression: InvocationExpressionSyntax arrowInv })
            topLevelInvocations = [arrowInv];

        foreach (var invRoot in topLevelInvocations)
        {
            var calls = FlattenCalls(invRoot).ToList();
            if (!calls.Any(c => c.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "DefineGroup" }))
                continue;
            ProcessInvocationChain(GetOrAddGroup, null, calls, semanticModel);
        }

        foreach (var g in groupsRoot)
            yield return g;
        yield break;

        // local helper kept below for ProcessInvocationChain
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

        // class-level helper declared below
    }

    private static ArgumentSyntax? GetBuilderLambdaArgumentIfAny(IMethodSymbol methodSymbol,
        InvocationExpressionSyntax call)
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

        // 语义识别成功
        if (paramIndex >= 0)
        {
            var args = call.ArgumentList.Arguments;
            return paramIndex >= args.Count ? null : args[paramIndex];
        }

        // 回退：仅按语法寻找调用中的 lambda 参数（通常为最后一个）
        // 这样即便语义模型未能解析到 Action<PermissionGroupBuilder<...>> 也能继续遍历组内定义。
        var fallbacks = call.ArgumentList.Arguments;
        for (var i = fallbacks.Count - 1; i >= 0; i--)
        {
            var expr = fallbacks[i].Expression;
            if (expr is ParenthesizedLambdaExpressionSyntax or SimpleLambdaExpressionSyntax)
                return fallbacks[i];
        }

        return null;
    }

    private void ProcessBuilderLambda(GroupDef current, ArgumentSyntax lambdaArg, SemanticModel semanticModel,
        Func<GroupDef?, string, Dictionary<string, ConstValue>, GroupDef> getOrAddGroup)
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

        switch (body)
        {
            case BlockSyntax block:
            {
                foreach (var stmt in block.Statements.OfType<ExpressionStatementSyntax>())
                    if (stmt.Expression is InvocationExpressionSyntax inv)
                    {
                        var calls = FlattenCalls(inv);
                        ProcessInvocationChain(getOrAddGroup, current, calls, semanticModel);
                    }

                break;
            }
            case ExpressionSyntax exprBody:
            {
                if (exprBody is InvocationExpressionSyntax inv)
                {
                    var calls = FlattenCalls(inv);
                    ProcessInvocationChain(getOrAddGroup, current, calls, semanticModel);
                }

                break;
            }
        }
    }

    private void ProcessInvocationChain(
        Func<GroupDef?, string, Dictionary<string, ConstValue>, GroupDef> getOrAddGroup,
        GroupDef? current,
        IEnumerable<InvocationExpressionSyntax> calls,
        SemanticModel sm)
    {
        var stack = new Stack<GroupDef>();
        if (current is not null) stack.Push(current);

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
                    var parsed = ParseGroupOrPermissionArguments(sm, args);
                    if (parsed.LogicalName is null) continue;
                    var parent = stack.Count == 0 ? null : stack.Peek();
                    var grp = getOrAddGroup(parent, parsed.LogicalName, parsed.Props);
                    grp.DisplayName ??= parsed.DisplayName ?? parsed.LogicalName;
                    grp.Description ??= parsed.Description;
                    stack.Push(grp);

                    // 查找 builder-lambda 参数：优先语义，再回退语法
                    ArgumentSyntax? builderLambdaArg = null;
                    if (sm.GetSymbolInfo(call).Symbol is IMethodSymbol methodSymbol)
                        builderLambdaArg = GetBuilderLambdaArgumentIfAny(methodSymbol, call);

                    if (builderLambdaArg is null)
                    {
                        var argsList = call.ArgumentList.Arguments;
                        for (var i = argsList.Count - 1; i >= 0; i--)
                        {
                            var e = argsList[i].Expression;
                            if (e is ParenthesizedLambdaExpressionSyntax or SimpleLambdaExpressionSyntax)
                            {
                                builderLambdaArg = argsList[i];
                                break;
                            }
                        }
                    }

                    if (builderLambdaArg is not null)
                    {
                        ProcessBuilderLambda(grp, builderLambdaArg, sm, getOrAddGroup);
                        if (stack.Count > 0) stack.Pop();
                    }

                    break;
                }
                case "WithOptions":
                {
                    if (stack.Count == 0) break;
                    var args = call.ArgumentList.Arguments;
                    if (args.Count > 0)
                    {
                        var cur = stack.Peek();
                        ExtractAssignmentsFromLambda(sm, args[0].Expression, cur.Props);
                    }

                    break;
                }
                case "AddPermission":
                {
                    if (stack.Count == 0) break;
                    var args = call.ArgumentList.Arguments;
                    if (args.Count == 0) break;
                    var parsed = ParseGroupOrPermissionArguments(sm, args);
                    if (parsed.LogicalName is null) break;
                    AddOrUpdatePermission(stack.Peek(), parsed.LogicalName, parsed.DisplayName, parsed.Description,
                        parsed.Props);
                    break;
                }
            }
        }
    }

    private (string? LogicalName, string? DisplayName, string? Description, Dictionary<string, ConstValue> Props)
        ParseGroupOrPermissionArguments(
            SemanticModel semanticModel,
            SeparatedSyntaxList<ArgumentSyntax> args)
    {
        string? logicalName = null;
        string? displayName = null;
        string? description = null;
        var props = new Dictionary<string, ConstValue>(StringComparer.Ordinal);

        if (args.Count > 0) logicalName = GetConstString(semanticModel, args[0].Expression);

        for (var i = 1; i < args.Count; i++)
        {
            var expr = args[i].Expression;
            var str = GetConstString(semanticModel, expr);
            if (str is not null)
            {
                if (displayName is null)
                {
                    displayName = str;
                    continue;
                }

                if (description is null)
                {
                    description = str;
                    continue;
                }
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
                    if (stmt.Expression is AssignmentExpressionSyntax assign)
                        TryCaptureAssignment(semanticModel, paramName, assign, into);

                break;
            }
            case ExpressionSyntax expr:
            {
                if (expr is AssignmentExpressionSyntax assign)
                    TryCaptureAssignment(semanticModel, paramName, assign, into);

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
        if (value is not null) into[propName] = value;
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

    private static void AddOrUpdatePermission(GroupDef parent, string name, string? displayName, string? description,
        Dictionary<string, ConstValue> props)
    {
        var existing =
            parent.Permissions.FirstOrDefault(p => string.Equals(p.LogicalName, name, StringComparison.Ordinal));
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