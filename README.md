# FluentPermissions

[中文文档](./README.zh-CN.md)

A compile-time, fluent, strongly-typed permission modeling toolkit for .NET.

- Contracts library (Core) with a minimal fluent builder interface
- Roslyn source generator that parses your fluent definitions and emits:
  - Rich runtime models (PermissionGroupInfo, PermissionItemInfo) with option properties, FullName, and a stable Key (dotted path like `System.Users.Create`)
  - A strongly-typed `AppPermissions` access API with hierarchical nested classes and a flat `Keys` map
- Sample console app demonstrating usage
- xUnit tests for property mapping and navigation

## Architecture

Projects in this repository:

- FluentPermissions.Core (netstandard2.0)
  - Contracts and fluent builder types (no runtime model classes shipped at runtime; models are generated)
- FluentPermissions (Source Generator, netstandard2.0)
  - Roslyn incremental generator that scans implementations of `IPermissionRegistrar<TGroupOptions,TPermissionOptions>`
  - Parses `Register(builder)` builder-lambda group scopes: `DefineGroup(..., builder => { builder.WithOptions(...); builder.DefineGroup(...); builder.AddPermission(...); })` (supports deeply nested groups)
  - Generates models and `AppPermissions` under `$(AssemblyName).Generated`
- FluentPermissions.Sample (net9.0, Exe)
  - Demonstrates defining hierarchical groups and consuming generated APIs
- FluentPermissions.Tests (net9.0)
  - Basic tests verifying property mapping and relationships

## Quick start

1) Add references

- Reference `FluentPermissions.Core` in your app
- Add the generator `FluentPermissions` as an Analyzer reference or package

2) Define your contracts

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

3) Implement a registrar (builder-lambda style)

```csharp
public class AppPermissionDefinition : IPermissionRegistrar<SampleGroupOptions, SamplePermissionOptions>
{
    public void Register(PermissionBuilder<SampleGroupOptions, SamplePermissionOptions> builder)
    {
    builder
      .DefineGroup("System", "System", "Core system settings", system =>
      {
        system.WithOptions(o =>
        {
          o.DisplayOrder = 10;
          o.Icon = "fa-gear";
        });

        system.DefineGroup("Users", "User Management", users =>
        {
          users.AddPermission("Create", "Create user");
          users.AddPermission("Delete", "Delete user", o => { o.IsHighRisk = true; });
        });

        system.DefineGroup("Roles", "Role Management", roles =>
        {
          roles.AddPermission("Assign", "Assign role");
        });
      })
      .DefineGroup("Reports", reports =>
      {
        reports.WithOptions(o => { o.DisplayOrder = 20; o.Icon = "fa-chart"; });
        reports.AddPermission("View", "View reports");
        reports.AddPermission("Export", "Export reports");
      });
    }
}
```

4) Consume the generated API

Namespace: `YourAssemblyName.Generated`

```csharp
using YourAssemblyName.Generated;

var system = AppPermissions.System.Group;                // PermissionGroupInfo
var users  = AppPermissions.System.Users.Group;          // PermissionGroupInfo
var create = AppPermissions.System.Users.Create;         // PermissionItemInfo
var key    = AppPermissions.Keys.System_Users_Create;    // Dotted path string

Console.WriteLine(create.FullName); // System_Users_Create
Console.WriteLine(create.Key);      // System.Users.Create
```

The `PermissionItemInfo` has an implicit `string` conversion returning `Key` (the dotted path) for convenient logging.

## Generated models

- PermissionGroupInfo
  - LogicalName, DisplayName, Description, FullName (underscored path), Key (dotted path string), ParentKey
  - Option properties from your group options (including inherited settable public properties)
  - Permissions (IReadOnlyList<PermissionItemInfo>), Children (IReadOnlyList<PermissionGroupInfo>)
- PermissionItemInfo
  - LogicalName, DisplayName, Description, FullName, Key, GroupKey
  - Option properties from your permission options

## Build, test, run

- Build solution
- Run tests
- Run sample app

The generator can optionally emit generated files to `obj/generated/...` when the consuming project turns on:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)\generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

## Notes

- Keys are stable across builds and equal to the dotted full path (e.g., `System.Users.Create`)
- Nested groups are defined inside the `DefineGroup(..., builder => { ... })` scope. Use `WithOptions(...)` to set group metadata.
- If your group name conflicts with `System`, we use fully-qualified `global::System` in generated code to avoid ambiguity

## Migration (breaking changes)

Starting from v1, the legacy chaining style and `.Then()` were removed in favor of a single, clearer DSL:

- Removed: non-lambda `DefineGroup(...)` overloads that returned a builder to be unwound with `.Then()`
- Added: builder-lambda overloads `DefineGroup(name, [display], [description], builder => { ... })`
- Added: `group.WithOptions(Action<TGroupOptions>)` for group metadata; keep `AddPermission` overloads for permission metadata

Before (legacy):

```csharp
builder
  .DefineGroup("System", o => { o.DisplayOrder = 10; })
    .DefineGroup("Users")
      .AddPermission("Create")
    .Then()
  .Then();
```

After (current):

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

MIT (pending).