# FluentPermissions

[English](./README.md)

一个基于 Roslyn 增量生成器的 .NET 编译期权限建模工具。通过简洁的 Fluent DSL 定义权限组与权限项，编译时生成强类型访问 API 与只读运行时模型，无需手写样板代码。

你将获得：
- 强类型访问入口 `AppPermissions`（嵌套类导航 + 扁平 Keys 常量）
- 运行时模型 `PermissionGroupInfo` / `PermissionItemInfo`（包含你的强类型选项属性）
- 稳定的 Key 与不可变集合（IReadOnlyList / IReadOnlyDictionary）

核心特性：
- 支持 builder-lambda 的 DSL：DefineGroup(..., builder => { ... })
- 组级元数据专用 `WithOptions(...)`；权限元数据在 `AddPermission(...)` 中配置
- 强类型 Options，编译期提取常量赋值
- 生成只读的权限集合；数据在 `AppPermissions` 静态构造中一次性构建

## 项目结构

- FluentPermissions.Core（netstandard2.0）：
  - 契约与 Fluent Builder（不包含运行时模型类型）
- FluentPermissions（源码生成器，netstandard2.0）：
  - 生成 `$(AssemblyName).Generated` 命名空间下的模型与 `AppPermissions`
- FluentPermissions.Sample（net9.0）：
  - 演示定义与消费
- FluentPermissions.Tests（net9.0）：
  - 覆盖结构、选项、只读性、键格式等

## 快速开始

1) 引用与安装
- 在消费项目中引用 `FluentPermissions.Core`
- 以 Analyzer 方式引入 `FluentPermissions` 源码生成器（NuGet 包或本地项目引用）

2) 定义选项（强类型）
```csharp
public sealed class SampleGroupOptions : PermissionOptionsBase
{
    public int DisplayOrder { get; set; }
    public string? Icon { get; set; }
}

public sealed class SamplePermissionOptions : PermissionOptionsBase
{
    public bool IsHighRisk { get; set; }
}
```

3) 实现注册器（仅 builder-lambda）
```csharp
public class AppPermissionDefinition : IPermissionRegistrar<SampleGroupOptions, SamplePermissionOptions>
{
    public void Register(PermissionBuilder<SampleGroupOptions, SamplePermissionOptions> builder)
    {
        builder
            .DefineGroup("System", "系统", "核心系统设置", system =>
            {
                system.WithOptions(o =>
                {
                    o.DisplayOrder = 10;
                    o.Icon = "fa-gear";
                });

                system.DefineGroup("Users", "用户账户管理", users =>
                {
                    users.AddPermission("Create", "创建用户");
                    users.AddPermission("Delete", "删除用户", "这是一个高风险操作", o => o.IsHighRisk = true);
                });

                system.DefineGroup("Roles", "角色管理", roles =>
                {
                    roles.AddPermission("Create", "创建角色");
                    roles.AddPermission("Assign", "分配角色");
                });
            })
            .DefineGroup("Reports", reports =>
            {
                reports.WithOptions(o => { o.DisplayOrder = 20; o.Icon = "fa-chart"; });
                reports.AddPermission("View", "查看报表");
                reports.AddPermission("Export", "导出报表");
            });
    }
}
```

4) 使用生成的 API（命名空间：`YourAssemblyName.Generated`）
```csharp
using YourAssemblyName.Generated;

var system = AppPermissions.System.Group;               // PermissionGroupInfo
var users  = AppPermissions.System.Users.Group;         // PermissionGroupInfo
var create = AppPermissions.System.Users.Create;        // PermissionItemInfo

Console.WriteLine(create.Key);                         // System_Users_Create
Console.WriteLine(AppPermissions.Keys.System_Users_Create); // System_Users_Create

// 只读字典/集合
var group = AppPermissions.GroupsByKey["System"];      // Key 为下划线：System
var perm  = AppPermissions.PermsByKey["System_Users_Create"];
```

## 生成内容与约定

命名空间：`$(AssemblyName).Generated`

- `internal static class PermissionModels`
  - `internal sealed class PermissionGroupInfo`
    - Key（string，下划线格式），ParentKey（string?）
    - LogicalName，DisplayName（未定义时默认 LogicalName），Description（默认 string.Empty）
    - 你的组选项属性（强类型）
    - SubGroups：IReadOnlyList<PermissionGroupInfo>
    - Permissions：IReadOnlyList<PermissionItemInfo>
  - `internal sealed class PermissionItemInfo`
    - Key（string，下划线格式），GroupKey（string，下划线）
    - LogicalName，DisplayName（默认 LogicalName），Description（默认 string.Empty）
    - 你的权限选项属性（强类型）

- `internal static class AppPermissions`
  - 嵌套类层级与 DSL 相同（如 `AppPermissions.System.Users.Create`）
  - `public static class Keys`：为每个组与权限生成常量，值为下划线 Key
  - `GroupsByKey` / `PermsByKey`：IReadOnlyDictionary<string, ...>
  - `GetAllGroups()`：IReadOnlyList<PermissionGroupInfo>

行为与默认值：
- Key/GroupKey/ParentKey 采用下划线格式（如 `A_B_C`）
- DisplayName 未提供时回退到 LogicalName
- Description 未提供时回退到 `string.Empty`
- 模型、集合、字典均为只读（不可在运行时修改）

## 备注

- 生成器包含一致性分析（如 FP0002：所有注册器须使用相同的泛型参数）。

## License

MIT