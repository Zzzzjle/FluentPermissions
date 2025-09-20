# FluentPermissions

[English](./README.md)

一个基于 Roslyn 源码生成的 .NET 编译期权限建模工具，用流式 API 定义权限，生成强类型访问代码与运行时模型。

- Core 契约库：最小化的流式构建接口
- 源码生成器：解析你的 Fluent 定义，生成
  - 运行时模型（PermissionGroupInfo / PermissionItemInfo），包含选项属性、FullName 与稳定 Key（与点分路径一致，如 `System.Users.Create`）
  - 强类型 `AppPermissions` 访问层：层级嵌套类 + 扁平 `Keys` 常量映射
- Sample 控制台应用演示
- xUnit 单元测试保障

## 架构

仓库包含如下项目：

- FluentPermissions.Core（netstandard2.0）
  - 契约与 Fluent Builder 类型（运行时模型由生成器产生，不在 Core 内内置）
- FluentPermissions（源码生成器，netstandard2.0）
  - 增量式生成器，扫描实现 `IPermissionRegistrar<TGroupOptions,TPermissionOptions>` 的类型
  - 解析 `Register(builder)` 中的“组内 builder-lambda 作用域”：`DefineGroup(..., builder => { builder.WithOptions(...); builder.DefineGroup(...); builder.AddPermission(...); })`（支持深度嵌套）
  - 在 `$(AssemblyName).Generated` 命名空间内生成模型与 `AppPermissions`
- FluentPermissions.Sample（net9.0，可执行）
  - 定义层级权限并消费生成的 API
- FluentPermissions.Tests（net9.0）
  - 基本属性映射与导航关系测试

## 快速开始

1) 引用

- 在你的应用中引用 `FluentPermissions.Core`
- 以 Analyzer 形式添加 `FluentPermissions` 生成器或通过包方式集成

2) 定义选项

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

3) 实现注册器（builder-lambda 风格）

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
          users.AddPermission("Delete", "删除用户", o => { o.IsHighRisk = true; });
        });

        system.DefineGroup("Roles", "角色管理", roles =>
        {
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

4) 使用生成的 API

命名空间：`YourAssemblyName.Generated`

```csharp
using YourAssemblyName.Generated;

var system = AppPermissions.System.Group;              // PermissionGroupInfo
var users  = AppPermissions.System.Users.Group;        // PermissionGroupInfo
var create = AppPermissions.System.Users.Create;       // PermissionItemInfo
var key    = AppPermissions.Keys.System_Users_Create;  // 点分路径字符串

Console.WriteLine(create.FullName); // System_Users_Create
Console.WriteLine(create.Key);      // System.Users.Create
```

`PermissionItemInfo` 支持隐式转换为 string（Key，点分路径），便于日志输出。

## 生成的模型

- PermissionGroupInfo
  - LogicalName、DisplayName、Description、FullName（下划线路径）、Key（点分路径字符串）、ParentKey
  - 从组选项类型收集的属性（包含继承的、Public settable）
  - Permissions（IReadOnlyList<PermissionItemInfo>）、Children（IReadOnlyList<PermissionGroupInfo>）
- PermissionItemInfo
  - LogicalName、DisplayName、Description、FullName、Key、GroupKey
  - 从权限选项类型收集的属性

## 构建 / 测试 / 运行

- 构建解决方案
- 运行测试
- 运行示例程序（Sample 项目是可执行）

可通过在消费项目中开启生成文件输出，查看生成内容：

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)\generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

## 说明

- Key 与点分全路径一致（如 `System.Users.Create`），跨构建保持稳定
- 通过 `DefineGroup(..., builder => { ... })` 在组内一次性定义结构与元数据，使用 `WithOptions(...)` 设置组级元数据
- 若组名为 `System`，生成代码会使用 `global::System` 前缀避免命名冲突

## 迁移（破坏性变更）

从当前版本起，移除了旧的链式 `.Then()` 风格与所有“非 lambda”的 `DefineGroup` 重载，统一到 builder-lambda DSL：

- 已移除：返回可链式的“非 lambda”`DefineGroup(...)`（靠 `.Then()` 回退层级）
- 已新增：`DefineGroup(name, [display], [description], builder => { ... })`
- 已新增：`group.WithOptions(Action<TGroupOptions>)` 专用于组级元数据；`AddPermission` 仍用于权限元数据

迁移前（旧）：

```csharp
builder
  .DefineGroup("System", o => { o.DisplayOrder = 10; })
    .DefineGroup("Users")
      .AddPermission("Create")
    .Then()
  .Then();
```

迁移后（新）：

```csharp
builder.DefineGroup("System", system =>
{
    system.WithOptions(o => o.DisplayOrder = 10);
    system.DefineGroup("Users", users =>
    {
        users.AddPermission("Create");
    });
});
```

## License

MIT（待定）