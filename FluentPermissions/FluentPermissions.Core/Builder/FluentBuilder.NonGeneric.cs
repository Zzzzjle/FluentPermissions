using System;

namespace FluentPermissions.Core.Builder;

/// <summary>
///     非泛型的权限构建器入口（无扩展字段/无 Options）。仅作为 DSL 占位以供源生成器解析。
/// </summary>
public sealed class PermissionBuilder
{
    /// <summary>
    ///     定义顶层权限组，并在 <paramref name="configureGroup" /> 中配置其子级与权限。
    /// </summary>
    public PermissionBuilder DefineGroup(string logicalName, Action<PermissionGroupBuilder> configureGroup)
    {
        var child = new PermissionGroupBuilder(logicalName);
        configureGroup(child);
        return this;
    }

    /// <summary>
    ///     定义顶层权限组（带显示名），并在 <paramref name="configureGroup" /> 中配置其子级与权限。
    /// </summary>
    public PermissionBuilder DefineGroup(string logicalName, string displayName,
        Action<PermissionGroupBuilder> configureGroup)
    {
        var child = new PermissionGroupBuilder(logicalName);
        configureGroup(child);
        return this;
    }

    /// <summary>
    ///     定义顶层权限组（带显示名与描述），并在 <paramref name="configureGroup" /> 中配置其子级与权限。
    /// </summary>
    public PermissionBuilder DefineGroup(string logicalName, string displayName, string description,
        Action<PermissionGroupBuilder> configureGroup)
    {
        var child = new PermissionGroupBuilder(logicalName);
        configureGroup(child);
        return this;
    }
}

/// <summary>
///     非泛型的权限组构建器（不提供 Options 配置）。
/// </summary>
public sealed class PermissionGroupBuilder
{
    internal PermissionGroupBuilder(string groupName)
    {
        GroupName = groupName;
    }

    /// <summary>
    ///     当前组的逻辑名称。
    /// </summary>
    public string GroupName { get; }

    // 嵌套组定义（仅 builder-lambda 形式）
    /// <summary>
    ///     在当前组下定义子组。
    /// </summary>
    public PermissionGroupBuilder DefineGroup(string logicalName, Action<PermissionGroupBuilder> configureGroup)
    {
        var child = new PermissionGroupBuilder(logicalName);
        configureGroup(child);
        return this;
    }

    /// <summary>
    ///     在当前组下定义子组（带显示名）。
    /// </summary>
    public PermissionGroupBuilder DefineGroup(string logicalName, string displayName,
        Action<PermissionGroupBuilder> configureGroup)
    {
        var child = new PermissionGroupBuilder(logicalName);
        configureGroup(child);
        return this;
    }

    /// <summary>
    ///     在当前组下定义子组（带显示名与描述）。
    /// </summary>
    public PermissionGroupBuilder DefineGroup(string logicalName, string displayName, string description,
        Action<PermissionGroupBuilder> configureGroup)
    {
        var child = new PermissionGroupBuilder(logicalName);
        configureGroup(child);
        return this;
    }

    // 权限定义（不支持 Options）
    /// <summary>
    ///     定义权限项。
    /// </summary>
    public PermissionGroupBuilder AddPermission(string logicalName)
    {
        return this;
    }

    /// <summary>
    ///     定义权限项（带显示名）。
    /// </summary>
    public PermissionGroupBuilder AddPermission(string logicalName, string displayName)
    {
        return this;
    }

    /// <summary>
    ///     定义权限项（带显示名与描述）。
    /// </summary>
    public PermissionGroupBuilder AddPermission(string logicalName, string displayName, string description)
    {
        return this;
    }
}