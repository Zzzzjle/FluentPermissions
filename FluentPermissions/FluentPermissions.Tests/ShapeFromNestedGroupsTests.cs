using System.Collections;
using System.Linq;
using FluentPermissions.Core.Abstractions;
using FluentPermissions.Core.Builder;
using Xunit;

namespace FluentPermissions.Tests;

public class SGroupOptions : PermissionOptionsBase
{
    public int DisplayOrder { get; set; }
    public string? Icon { get; set; }
}

public class SPermOptions : PermissionOptionsBase
{
    public bool IsHighRisk { get; set; }
}

// Reuse the same generic args as existing tests: TestGroupOptions/TestPermissionOptions
public class AppReg2 : IPermissionRegistrar<TestGroupOptions, TestPermissionOptions>
{
    public void Register(PermissionBuilder<TestGroupOptions, TestPermissionOptions> builder)
    {
        builder
            .DefineGroup("System", "系统", "核心系统设置", system =>
            {
                system.WithOptions(options => { options.Order = 10; });

                system.DefineGroup("Users", "用户账户管理", users =>
                {
                    users.AddPermission("Create", "创建用户");
                    users.AddPermission("Delete", "删除用户", "这是一个高风险操作", o => { o.Critical = true; });
                });

                system.DefineGroup("Roles", "角色管理", roles =>
                {
                    roles.AddPermission("Create", "创建角色");
                    roles.AddPermission("Assign", "分配角色");
                });
            })
            .DefineGroup("Reports", reports =>
            {
                reports.WithOptions(options => { options.Order = 20; });
                reports.AddPermission("View", "查看报表");
                reports.AddPermission("Export", "导出报表");
            });
    }
}

public class ShapeFromNestedGroupsTests
{
    [Fact]
    public void No_Spurious_Roots_And_Correct_Nesting()
    {
        var asm = typeof(ShapeFromNestedGroupsTests).Assembly;
        var appNs = asm.GetName().Name + ".Auth";
        var appPermissionsType = asm.GetType(appNs + ".AppPermissions");
        Assert.NotNull(appPermissionsType);

        // Root groups should include: System, Reports (other tests may add more roots)
        var getAll = appPermissionsType.GetMethod("GetAllGroups");
        Assert.NotNull(getAll);
        var roots = ((IEnumerable)getAll.Invoke(null, null)!).Cast<object>().ToList();
        var rootNames = roots.Select(r => (string)r.GetType().GetProperty("LogicalName")!.GetValue(r)!).ToList();
        Assert.Contains("System", rootNames);
        Assert.Contains("Reports", rootNames);
        Assert.DoesNotContain("Users", rootNames);
        Assert.DoesNotContain("Roles", rootNames);

        // System children must be Users and Roles (two children)
        var groupsByKeyField = appPermissionsType.GetField("GroupsByKey")!;
        var groupsByKeyObj = groupsByKeyField.GetValue(null)!;
        var gbkType = groupsByKeyField.FieldType;
        Assert.True(gbkType.IsGenericType);
        Assert.Equal("IReadOnlyDictionary`2", gbkType.GetGenericTypeDefinition().Name);

        // Same for PermsByKey
        var permsByKeyField = appPermissionsType.GetField("PermsByKey")!;
        var pbkType = permsByKeyField.FieldType;
        Assert.True(pbkType.IsGenericType);
        Assert.Equal("IReadOnlyDictionary`2", pbkType.GetGenericTypeDefinition().Name);

        // Access System group
        var groupsByKey = (IDictionary)groupsByKeyObj;
        var system = groupsByKey["System"]!;
        var children = ((IEnumerable)system.GetType().GetProperty("SubGroups")!.GetValue(system)!)
            .Cast<object>().ToList();
        // SubGroups is IReadOnlyList
        var childrenPropType = system.GetType().GetProperty("SubGroups")!.PropertyType;
        Assert.True(childrenPropType.IsGenericType);
        Assert.Equal("IReadOnlyList`1", childrenPropType.GetGenericTypeDefinition().Name);
        // Permissions is IReadOnlyList
        var permsPropType = system.GetType().GetProperty("Permissions")!.PropertyType;
        Assert.True(permsPropType.IsGenericType);
        Assert.Equal("IReadOnlyList`1", permsPropType.GetGenericTypeDefinition().Name);
        var childNames = children.Select(c => (string)c.GetType().GetProperty("LogicalName")!.GetValue(c)!).ToList();
        Assert.Equal(2, children.Count);
        Assert.Contains("Users", childNames);
        Assert.Contains("Roles", childNames);

        // Ensure Roles is under System, not under Users
        Assert.True(groupsByKey.Contains("System_Roles"));
        Assert.False(groupsByKey.Contains("System_Users_Roles"));

        // Keys class shouldn't contain System_Users_Roles constant
        var keysType = appPermissionsType.GetNestedType("Keys");
        Assert.NotNull(keysType);
        var spur = keysType.GetField("System_Users_Roles");
        Assert.Null(spur);

        // Order (typed option) set via registrar
        var sysOrderProp = system.GetType().GetProperty("Order");
        Assert.NotNull(sysOrderProp);
        Assert.Equal(10, (int)sysOrderProp.GetValue(system)!);
        var reports = groupsByKey["Reports"]!;
        var repOrderProp = reports.GetType().GetProperty("Order");
        Assert.NotNull(repOrderProp);
        Assert.Equal(20, (int)repOrderProp.GetValue(reports)!);
    }
}