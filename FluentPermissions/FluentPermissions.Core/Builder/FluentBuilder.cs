using System;
using FluentPermissions.Core.Abstractions;

namespace FluentPermissions.Core.Builder;

public sealed class PermissionBuilder<TGroupOptions, TPermissionOptions>
    where TGroupOptions : PermissionOptionsBase, new()
    where TPermissionOptions : PermissionOptionsBase, new()
{
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> DefineGroup(
        string name, Action<TGroupOptions>? configure = null)
    {
        // 这里只做契约；源生成器会读取调用图。
        return new PermissionGroupBuilder<TGroupOptions, TPermissionOptions>(name);
    }
}

public sealed class PermissionGroupBuilder<TGroupOptions, TPermissionOptions>
    where TGroupOptions : PermissionOptionsBase, new()
    where TPermissionOptions : PermissionOptionsBase, new()
{
    public string GroupName { get; }
    internal PermissionGroupBuilder(string groupName) { GroupName = groupName; }

    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> AddPermission(
        string name, Action<TPermissionOptions>? configure = null)
    {
        // 这里只做契约；源生成器会读取调用图。
        return this;
    }

    // 允许在当前组下继续定义子组
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> DefineGroup(
        string name, Action<TGroupOptions>? configure = null)
    {
        // 这里只做契约；源生成器会读取调用图。
        return new PermissionGroupBuilder<TGroupOptions, TPermissionOptions>(name);
    }

    // 结束当前组定义，回到父级（如果存在）；为了保持链式编译通过，这里返回同类型。
    public PermissionGroupBuilder<TGroupOptions, TPermissionOptions> Then()
        => this;
}