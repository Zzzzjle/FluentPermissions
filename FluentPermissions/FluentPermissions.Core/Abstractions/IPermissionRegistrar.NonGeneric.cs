using FluentPermissions.Core.Builder;

namespace FluentPermissions.Core.Abstractions;

/// <summary>
///     非泛型权限注册器（无扩展字段/无 Options）。实现该接口的类型将被源生成器扫描。
/// </summary>
public interface IPermissionRegistrar
{
    /// <summary>
    ///     在此方法中使用非泛型 DSL 定义权限树（不支持 Options 配置）。
    /// </summary>
    /// <param name="builder">非泛型权限构建器。</param>
    void Register(PermissionBuilder builder);
}