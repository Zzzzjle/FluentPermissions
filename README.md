# FluentPermissions

[简体中文](./README.zh-CN.md)

A Roslyn incremental source generator for modeling permissions in .NET. Define permission groups and items with a concise fluent DSL, and get strong-typed accessors plus immutable runtime models generated at compile time.

What you get:
- Strong-typed `AppPermissions` entry (nested navigation + flat Keys constants)
- Runtime models `PermissionGroupInfo` / `PermissionItemInfo` (including your strongly-typed option properties)
- Stable keys and immutable collections (IReadOnlyList / IReadOnlyDictionary)

Highlights:
- Lambda-based DSL: DefineGroup(..., builder => { ... })
- Group-level metadata via `WithOptions(...)`; permission metadata in `AddPermission(...)`
- Strongly-typed Options (no dictionaries), captured at compile time
- Generated collections/dictionaries are read-only; data is built once in `AppPermissions` static constructor

## Repository layout

- FluentPermissions.Core (netstandard2.0):
  - Contracts and fluent builder types (runtime models are generated)
- FluentPermissions (source generator, netstandard2.0):
  - Emits models and `AppPermissions` under `$(AssemblyName).Generated`
- FluentPermissions.Sample (net9.0):
  - Shows how to define and consume
- FluentPermissions.Tests (net9.0):
  - Covers structure, options, immutability, key format, etc.

## Quick start

1) Reference & install
- Reference `FluentPermissions.Core` in the consumer project
- Add the `FluentPermissions` source generator (as analyzer or via NuGet)

2) Define options (strongly-typed)
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

3) Implement a registrar (lambda-only)
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

                system.DefineGroup("Users", "User management", users =>
                {
                    users.AddPermission("Create", "Create user");
                    users.AddPermission("Delete", "Delete user", "High risk action", o => o.IsHighRisk = true);
                });

                system.DefineGroup("Roles", "Role management", roles =>
                {
                    roles.AddPermission("Create", "Create role");
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

4) Consume the generated API (namespace: `YourAssemblyName.Generated`)
```csharp
using YourAssemblyName.Generated;

var system = AppPermissions.System.Group;                 // PermissionGroupInfo
var users  = AppPermissions.System.Users.Group;           // PermissionGroupInfo
var create = AppPermissions.System.Users.Create;          // PermissionItemInfo

Console.WriteLine(create.Key);                            // System_Users_Create
Console.WriteLine(AppPermissions.Keys.System_Users_Create); // System_Users_Create

// Immutable dictionaries/collections
var group = AppPermissions.GroupsByKey["System"];        // underscore keys
var perm  = AppPermissions.PermsByKey["System_Users_Create"];
```

## Emitted artifacts & conventions

Namespace: `$(AssemblyName).Generated`

- `internal static class PermissionModels`
  - `internal sealed class PermissionGroupInfo`
    - Key (string, underscore), ParentKey (string?)
    - LogicalName, DisplayName (defaults to LogicalName), Description (defaults to string.Empty)
    - Your group option properties (strongly-typed)
    - SubGroups: IReadOnlyList<PermissionGroupInfo>
    - Permissions: IReadOnlyList<PermissionItemInfo>
  - `internal sealed class PermissionItemInfo`
    - Key (string, underscore), GroupKey (string, underscore)
    - LogicalName, DisplayName (defaults to LogicalName), Description (defaults to string.Empty)
    - Your permission option properties (strongly-typed)

- `internal static class AppPermissions`
  - Nested classes mirror your DSL (e.g., `AppPermissions.System.Users.Create`)
  - `public static class Keys`: constants for each group & permission, values are underscore keys
  - `GroupsByKey` / `PermsByKey`: IReadOnlyDictionary<string, ...>
  - `GetAllGroups()`: IReadOnlyList<PermissionGroupInfo>

Defaults & behavior:
- Key/GroupKey/ParentKey use underscore format (`A_B_C`)
- DisplayName defaults to LogicalName, Description defaults to `string.Empty`
- Models, collections, and dictionaries are read-only

## Inspect generated files (optional)

In the consuming project you can enable generated file output:
```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)\generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

## Notes

- Analyzer checks include consistency (e.g., FP0002: all registrars must use the same generic arguments).

## License

MIT