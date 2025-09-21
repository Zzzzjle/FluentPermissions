using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace FluentPermissions;

internal enum ConstKind
{
    String,
    Bool,
    Int,
    Double,
    Null
}

internal sealed class ConstValue(ConstKind kind, object? value)
{
    public ConstKind Kind { get; } = kind;
    public object? Value { get; } = value;

    public string ToEmitLiteral()
    {
        return Kind switch
        {
            ConstKind.String => Value is null
                ? "null"
                : "\"" + PermissionSourceGenerator.EscapeString((string)Value) + "\"",
            ConstKind.Bool => ((bool)Value!).ToString().ToLowerInvariant(),
            ConstKind.Int => Value!.ToString()!,
            ConstKind.Double => ((double)Value!).ToString(CultureInfo.InvariantCulture),
            _ => "null"
        };
    }
}

internal sealed class PermissionDef(
    string logicalName,
    string? displayName,
    string? description,
    Dictionary<string, ConstValue>? props = null)
{
    public string LogicalName { get; } = logicalName;
    public string? DisplayName { get; set; } = displayName;
    public string? Description { get; set; } = description;

    public Dictionary<string, ConstValue> Props { get; } =
        props ?? new Dictionary<string, ConstValue>(StringComparer.Ordinal);
}

internal sealed class GroupDef(
    string logicalName,
    string? displayName,
    string? description,
    Dictionary<string, ConstValue>? props = null,
    List<PermissionDef>? permissions = null,
    List<GroupDef>? children = null)
{
    public string LogicalName { get; } = logicalName;
    public string? DisplayName { get; set; } = displayName;
    public string? Description { get; set; } = description;

    public Dictionary<string, ConstValue> Props { get; } =
        props ?? new Dictionary<string, ConstValue>(StringComparer.Ordinal);

    public List<PermissionDef> Permissions { get; } = permissions ?? new List<PermissionDef>();
    public List<GroupDef> Children { get; } = children ?? new List<GroupDef>();
}

internal sealed class RegistrarInfo(INamedTypeSymbol symbol, INamedTypeSymbol @interface)
{
    public INamedTypeSymbol Symbol { get; } = symbol;
    public INamedTypeSymbol Interface { get; } = @interface;
}

internal sealed class OptionProp(string name, ConstKind kind)
{
    public string Name { get; } = name;
    public ConstKind Kind { get; } = kind;
}

internal sealed class Model(
    Compilation compilation,
    ImmutableArray<GroupDef> rootGroups,
    ImmutableArray<Diagnostic> diagnostics,
    bool hasFatal,
    ImmutableArray<OptionProp>? groupOptionProps = null,
    ImmutableArray<OptionProp>? permOptionProps = null)
{
    public Compilation Compilation { get; } = compilation;
    public ImmutableArray<GroupDef> RootGroups { get; } = rootGroups;
    public ImmutableArray<Diagnostic> Diagnostics { get; } = diagnostics;
    public bool HasFatal { get; } = hasFatal;
    public ImmutableArray<OptionProp> GroupOptionProps { get; } = groupOptionProps ?? ImmutableArray<OptionProp>.Empty;
    public ImmutableArray<OptionProp> PermOptionProps { get; } = permOptionProps ?? ImmutableArray<OptionProp>.Empty;
}