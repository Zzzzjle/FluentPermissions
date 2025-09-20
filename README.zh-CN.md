# FluentPermissions

[English](./README.md)

一个基于 Roslyn 源码生成的 .NET 编译期权限建模工具，用流式 API 定义权限，生成强类型访问代码与运行时模型。

- Core 契约库：最小化的流式构建接口
- 源码生成器：解析你的 Fluent 定义，生成
  - 运行时模型（PermissionGroupInfo / PermissionItemInfo），包含选项属性、FullName 与稳定 Key（SHA-256）
  - 强类型 `AppPermissions` 访问层，层级嵌套类、点分 Names 常量、全局扁平 Keys 常量映射
- Sample 控制台应用演示
- xUnit 单元测试保障

## 架构

仓库包含如下项目：

- FluentPermissions.Core（netstandard2.0）
  - 契约与 Fluent Builder 类型（运行时模型由生成器产生，不在 Core 内内置）
- FluentPermissions（源码生成器，netstandard2.0）
  - 增量式生成器，扫描实现 `IPermissionRegistrar<TGroupOptions,TPermissionOptions>` 的类型
  - 解析 `Register(builder)` 中的 Fluent 链：`DefineGroup(...).AddPermission(...).Then()`（支持无限嵌套）
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

3) 实现注册器

```csharp
public class AppPermissionDefinition : IPermissionRegistrar<SampleGroupOptions, SamplePermissionOptions>
{
    public void Register(PermissionBuilder<SampleGroupOptions, SamplePermissionOptions> builder)
    {
        builder
            .DefineGroup("System", o => { o.Description = "系统"; o.DisplayOrder = 10; })
                .DefineGroup("Users", o => { o.Description = "用户"; })
                    .AddPermission("Create", o => o.Description = "创建用户")
                    .AddPermission("Delete", o => { o.Description = "删除用户"; o.IsHighRisk = true; })
                .Then()
                .DefineGroup("Roles", o => o.Description = "角色")
                    .AddPermission("Assign", o => o.Description = "分配角色")
                .Then()
            .Then();
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
var key    = AppPermissions.Keys.System_Users_Create;  // 点分路径的 SHA-256 十六进制

Console.WriteLine(create.FullName); // System_Users_Create
Console.WriteLine(create.Key);      // cad9e2...
```

`PermissionItemInfo` 支持隐式转换为 string（Name），便于日志输出。

## 生成的模型

- PermissionGroupInfo
  - Name、FullName（下划线路径）、Key（点分路径 SHA-256）
  - 从组选项类型收集的属性（包含继承的、Public settable）
  - Permissions（IReadOnlyList<PermissionItemInfo>）、Children（IReadOnlyList<PermissionGroupInfo>）
- PermissionItemInfo
  - Name、GroupName、FullName、Key、Group
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

- Key 由点分全路径（如 `System.Users.Create`）稳定计算，跨构建保持一致
- 通过重复 `DefineGroup(...)` 与 `Then()` 实现无限嵌套的层级结构
- 若组名为 `System`，生成代码会使用 `global::System` 前缀避免命名冲突

## License

MIT（待定）