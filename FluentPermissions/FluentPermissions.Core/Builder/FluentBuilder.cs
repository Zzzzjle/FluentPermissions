using System;
using FluentPermissions.Core.Abstractions;

namespace FluentPermissions.Core.Builder;

/// <summary>
///     权限构建器的入口点，用于定义顶层权限组。
/// </summary>
/// <typeparam name="TGroupOptions"></typeparam>
/// <typeparam name="TPermissionOptions"></typeparam>
public sealed class PermissionBuilder<TGroupOptions, TPermissionOptions>
    where TGroupOptions : PermissionOptionsBase, new()
    where TPermissionOptions : PermissionOptionsBase, new()
{
    /// <summary>
    ///     定义顶层权限组，并在提供的 <paramref name="configureGroup" /> lambda 作用域内完成组内结构与元数据配置。
    ///     该方法返回父级 <see cref="PermissionBuilder{TGroupOptions,TPermissionOptions}" /> 以便继续定义其他顶层组。
    /// </summary>
    public PermissionBuilder<TGroupOptions, TPermissionOptions> DefineGroup(
        string logicalName,
        Action<PermissionGroupBuilder<TGroupOptions, TPermissionOptions>> configureGroup)
    {
        var child = new PermissionGroupBuilder<TGroupOptions, TPermissionOptions>(logicalName);
        configureGroup(child);
        return this;
    }

    /// <summary>
    ///     定义顶层权限组（带显示名），并在 <paramref name="configureGroup" /> 中配置组内结构与元数据。
    /// </summary>
    public PermissionBuilder<TGroupOptions, TPermissionOptions> DefineGroup(
        string logicalName,
        string displayName,
        Action<PermissionGroupBuilder<TGroupOptions, TPermissionOptions>> configureGroup)
    {
        var child = new PermissionGroupBuilder<TGroupOptions, TPermissionOptions>(logicalName);
        configureGroup(child);
        return this;
    }

    /// <summary>
    ///     定义顶层权限组（带显示名与描述），并在 <paramref name="configureGroup" /> 中配置组内结构与元数据。
    /// </summary>
    public PermissionBuilder<TGroupOptions, TPermissionOptions> DefineGroup(
        string logicalName,
        string displayName,
        string description,
        Action<PermissionGroupBuilder<TGroupOptions, TPermissionOptions>> configureGroup)
    {
        var child = new PermissionGroupBuilder<TGroupOptions, TPermissionOptions>(logicalName);
        configureGroup(child);
        return this;
    }
}

/// <summary>
///     表示一个权限组的构建器，用于继续定义子组或权限项。
/// </summary>
public sealed class PermissionGroupBuilder<TGroupOptions, TPermissionOptions>
    where TGroupOptions : PermissionOptionsBase, new()
    where TPermissionOptions : PermissionOptionsBase, new()
{
    internal PermissionGroupBuilder(string groupName)
    {
        GroupName = groupName;
    }

    /// <summary>
    ///     当前组的逻辑名称。
    /// </summary>
    public string GroupName { get; }

    /// <summary>
    ///     配置当前权限组的元数据（扩展属性）。
    ///     该方法的主要作用是为源生成器提供一个明确的信号，以采集组级选项的常量赋值。
    /// </summary>
    /// <param name="configureOptions">用于配置 <typeparamref name="TGroupOptions" /> 的委托。</param>
    /// <returns>返回当前构建器以支持链式调用。</returns>
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> WithOptions(Action<TGroupOptions> configureOptions)
    {
        // 运行时无需保存任何状态；源生成器会从 lambda 中提取常量赋值。
        var options = new TGroupOptions();
        configureOptions(options);
        // 不做持久化，保持无副作用。
        return this;
    }

    /// <summary>
    ///     定义权限项（最简形式）。
    /// </summary>
    // AddPermission 重载（同 DefineGroup 的模式）
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> AddPermission(string logicalName)
    {
        return this;
    }

    /// <summary>
    ///     定义权限项并指定显示名。
    /// </summary>
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> AddPermission(string logicalName,
        string displayName)
    {
        return this;
    }

    /// <summary>
    ///     定义权限项并指定显示名与描述。
    /// </summary>
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> AddPermission(string logicalName,
        string displayName, string description)
    {
        return this;
    }

    /// <summary>
    ///     定义权限项，仅配置扩展属性。
    /// </summary>
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> AddPermission(string logicalName,
        Action<TPermissionOptions> configure)
    {
        return this;
    }

    /// <summary>
    ///     定义权限项，指定显示名并配置扩展属性。
    /// </summary>
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> AddPermission(string logicalName,
        string displayName, Action<TPermissionOptions> configure)
    {
        return this;
    }

    /// <summary>
    ///     定义权限项，最完整重载。
    /// </summary>
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> AddPermission(string logicalName,
        string displayName, string description, Action<TPermissionOptions> configure)
    {
        return this;
    }

    // 旧的链式子组 DefineGroup 风格已移除，保留仅支持 builder-lambda 的重载。

    /// <summary>
    ///     在当前组下定义子组，并在提供的 <paramref name="configureGroup" /> lambda 内完成该子组的结构与元数据配置。
    ///     返回当前组的构建器，便于继续在同一层级追加定义。
    /// </summary>
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> DefineGroup(
        string logicalName,
        Action<PermissionGroupBuilder<TGroupOptions, TPermissionOptions>> configureGroup)
    {
        var child = new PermissionGroupBuilder<TGroupOptions, TPermissionOptions>(logicalName);
        configureGroup(child);
        return this;
    }

    /// <summary>
    ///     在当前组下定义子组（带显示名），并在 <paramref name="configureGroup" /> 中配置其结构与元数据。
    /// </summary>
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> DefineGroup(
        string logicalName,
        string displayName,
        Action<PermissionGroupBuilder<TGroupOptions, TPermissionOptions>> configureGroup)
    {
        var child = new PermissionGroupBuilder<TGroupOptions, TPermissionOptions>(logicalName);
        configureGroup(child);
        return this;
    }

    /// <summary>
    ///     在当前组下定义子组（带显示名与描述），并在 <paramref name="configureGroup" /> 中配置其结构与元数据。
    /// </summary>
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> DefineGroup(
        string logicalName,
        string displayName,
        string description,
        Action<PermissionGroupBuilder<TGroupOptions, TPermissionOptions>> configureGroup)
    {
        var child = new PermissionGroupBuilder<TGroupOptions, TPermissionOptions>(logicalName);
        configureGroup(child);
        return this;
    }
}