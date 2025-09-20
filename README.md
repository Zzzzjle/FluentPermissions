# FluentPermissions

[中文文档](./README.zh-CN.md)

A compile-time, fluent, strongly-typed permission modeling toolkit for .NET.

- Contracts library (Core) with a minimal fluent builder interface
- Roslyn source generator that parses your fluent definitions and emits:
  - Rich runtime models (PermissionGroupInfo, PermissionItemInfo) with option properties, FullName, and a stable Key (SHA-256)
  - A strongly-typed `AppPermissions` access API with hierarchical nested classes, dotted Names constants, and a flat Keys map
- Sample console app demonstrating usage
- xUnit tests for property mapping and navigation

## Architecture

Projects in this repository:

- FluentPermissions.Core (netstandard2.0)
  - Contracts and fluent builder types (no runtime model classes shipped at runtime; models are generated)
- FluentPermissions (Source Generator, netstandard2.0)
  - Roslyn incremental generator that scans implementations of `IPermissionRegistrar<TGroupOptions,TPermissionOptions>`
  - Parses `Register(builder)` fluent chains: `DefineGroup(...).AddPermission(...).Then()` (supports nested groups)
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

3) Implement a registrar

```csharp
public class AppPermissionDefinition : IPermissionRegistrar<SampleGroupOptions, SamplePermissionOptions>
{
    public void Register(PermissionBuilder<SampleGroupOptions, SamplePermissionOptions> builder)
    {
        builder
            .DefineGroup("System", o => { o.Description = "System"; o.DisplayOrder = 10; })
                .DefineGroup("Users", o => { o.Description = "Users"; })
                    .AddPermission("Create", o => o.Description = "Create user")
                    .AddPermission("Delete", o => { o.Description = "Delete user"; o.IsHighRisk = true; })
                .Then()
                .DefineGroup("Roles", o => o.Description = "Roles")
                    .AddPermission("Assign", o => o.Description = "Assign role")
                .Then()
            .Then();
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
var key    = AppPermissions.Keys.System_Users_Create;    // SHA-256 hex of dotted path

Console.WriteLine(create.FullName); // System_Users_Create
Console.WriteLine(create.Key);      // cad9e2...
```

The `PermissionItemInfo` has an implicit `string` conversion returning `Name` for convenient logging.

## Generated models

- PermissionGroupInfo
  - Name, FullName (underscored path), Key (SHA-256 of dotted path)
  - Option properties from your group options (including inherited settable public properties)
  - Permissions (IReadOnlyList<PermissionItemInfo>), Children (IReadOnlyList<PermissionGroupInfo>)
- PermissionItemInfo
  - Name, GroupName, FullName, Key, Group
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

- Keys are stable across builds and derived from the dotted full path (e.g., `System.Users.Create`)
- Nested groups are supported via repeated `DefineGroup(...)` and unwinding with `Then()`
- If your group name conflicts with `System`, we use fully-qualified `global::System` in generated code to avoid ambiguity

## License

MIT (pending).