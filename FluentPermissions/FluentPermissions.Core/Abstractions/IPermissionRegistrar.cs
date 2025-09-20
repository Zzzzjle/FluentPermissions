using FluentPermissions.Core.Builder;

namespace FluentPermissions.Core.Abstractions;

/// <summary>
/// 权限注册器接口。实现此接口的类将被源生成器扫描。
/// </summary>
public interface IPermissionRegistrar<TGroupOptions, TPermissionOptions>
    where TGroupOptions : PermissionOptionsBase, new()
    where TPermissionOptions : PermissionOptionsBase, new()
{
    void Register(PermissionBuilder<TGroupOptions, TPermissionOptions> builder);
}