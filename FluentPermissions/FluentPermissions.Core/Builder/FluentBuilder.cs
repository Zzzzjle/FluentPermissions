using System;
using FluentPermissions.Core.Abstractions;

namespace FluentPermissions.Core.Builder;

/// <summary>
/// 权限构建器的入口点，用于定义顶层权限组。
/// </summary>
/// <typeparam name="TGroupOptions"></typeparam>
/// <typeparam name="TPermissionOptions"></typeparam>
public sealed class PermissionBuilder<TGroupOptions, TPermissionOptions>
    where TGroupOptions : PermissionOptionsBase, new()
    where TPermissionOptions : PermissionOptionsBase, new()
{
    /// <summary>
    /// 定义顶层权限组（最简形式）。DisplayName 将回退为 logicalName。
    /// </summary>
    // 1) 最简洁 (DisplayName 回退为 logicalName)
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> DefineGroup(string logicalName)
        => new PermissionGroupBuilder<TGroupOptions, TPermissionOptions>(logicalName);

    /// <summary>
    /// 定义顶层权限组并指定显示名。
    /// </summary>
    // 2) 提供显示名
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> DefineGroup(string logicalName, string displayName)
        => new PermissionGroupBuilder<TGroupOptions, TPermissionOptions>(logicalName);

    /// <summary>
    /// 定义顶层权限组并指定显示名和描述。
    /// </summary>
    // 3) 提供显示名和描述
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> DefineGroup(string logicalName, string displayName, string? description)
        => new PermissionGroupBuilder<TGroupOptions, TPermissionOptions>(logicalName);

    /// <summary>
    /// 定义顶层权限组，仅配置扩展属性。
    /// </summary>
    // 4) 只配置扩展属性
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> DefineGroup(string logicalName, Action<TGroupOptions> configure)
        => new PermissionGroupBuilder<TGroupOptions, TPermissionOptions>(logicalName);

    /// <summary>
    /// 定义顶层权限组，指定显示名并配置扩展属性。
    /// </summary>
    // 5) 提供显示名并配置扩展属性
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> DefineGroup(string logicalName, string displayName, Action<TGroupOptions> configure)
        => new PermissionGroupBuilder<TGroupOptions, TPermissionOptions>(logicalName);

    /// <summary>
    /// 定义顶层权限组，最完整重载。
    /// </summary>
    // 6) 最完整重载
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> DefineGroup(string logicalName, string displayName, string? description, Action<TGroupOptions> configure)
        => new PermissionGroupBuilder<TGroupOptions, TPermissionOptions>(logicalName);
}

/// <summary>
/// 表示一个权限组的构建器，用于继续定义子组或权限项。
/// </summary>
public sealed class PermissionGroupBuilder<TGroupOptions, TPermissionOptions>
    where TGroupOptions : PermissionOptionsBase, new()
    where TPermissionOptions : PermissionOptionsBase, new()
{
    /// <summary>
    /// 当前组的逻辑名称。
    /// </summary>
    public string GroupName { get; }
    internal PermissionGroupBuilder(string groupName) { GroupName = groupName; }

    /// <summary>
    /// 定义权限项（最简形式）。
    /// </summary>
    // AddPermission 重载（同 DefineGroup 的模式）
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> AddPermission(string logicalName)
        => this;

    /// <summary>
    /// 定义权限项并指定显示名。
    /// </summary>
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> AddPermission(string logicalName, string displayName)
        => this;

    /// <summary>
    /// 定义权限项并指定显示名与描述。
    /// </summary>
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> AddPermission(string logicalName, string displayName, string? description)
        => this;

    /// <summary>
    /// 定义权限项，仅配置扩展属性。
    /// </summary>
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> AddPermission(string logicalName, Action<TPermissionOptions> configure)
        => this;

    /// <summary>
    /// 定义权限项，指定显示名并配置扩展属性。
    /// </summary>
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> AddPermission(string logicalName, string displayName, Action<TPermissionOptions> configure)
        => this;

    /// <summary>
    /// 定义权限项，最完整重载。
    /// </summary>
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> AddPermission(string logicalName, string displayName, string? description, Action<TPermissionOptions> configure)
        => this;

    // 允许在当前组下继续定义子组
    /// <summary>
    /// 在当前组下定义子组（最简）。
    /// </summary>
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> DefineGroup(string logicalName)
        => new PermissionGroupBuilder<TGroupOptions, TPermissionOptions>(logicalName);

    /// <summary>
    /// 在当前组下定义子组并指定显示名。
    /// </summary>
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> DefineGroup(string logicalName, string displayName)
        => new PermissionGroupBuilder<TGroupOptions, TPermissionOptions>(logicalName);

    /// <summary>
    /// 在当前组下定义子组并指定显示名和描述。
    /// </summary>
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> DefineGroup(string logicalName, string displayName, string? description)
        => new PermissionGroupBuilder<TGroupOptions, TPermissionOptions>(logicalName);

    /// <summary>
    /// 在当前组下定义子组，仅配置扩展属性。
    /// </summary>
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> DefineGroup(string logicalName, Action<TGroupOptions> configure)
        => new PermissionGroupBuilder<TGroupOptions, TPermissionOptions>(logicalName);

    /// <summary>
    /// 在当前组下定义子组，指定显示名并配置扩展属性。
    /// </summary>
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> DefineGroup(string logicalName, string displayName, Action<TGroupOptions> configure)
        => new PermissionGroupBuilder<TGroupOptions, TPermissionOptions>(logicalName);

    /// <summary>
    /// 在当前组下定义子组，最完整重载。
    /// </summary>
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> DefineGroup(string logicalName, string displayName, string? description, Action<TGroupOptions> configure)
        => new PermissionGroupBuilder<TGroupOptions, TPermissionOptions>(logicalName);

    /// <summary>
    /// 结束当前组定义，回到父级（如果存在）；为了保持链式编译通过，这里返回同类型。
    /// </summary>
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> Then()
        => this;
}