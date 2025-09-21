using System.Collections;
using System.Linq;
using FluentPermissions.Core.Abstractions;
using FluentPermissions.Core.Builder;
using Xunit;

namespace FluentPermissions.Tests;

// Registrar 1: Sales domain
public class SalesRegistrar : IPermissionRegistrar<TestGroupOptions, TestPermissionOptions>
{
    public void Register(PermissionBuilder<TestGroupOptions, TestPermissionOptions> builder)
    {
        builder.DefineGroup("Sales", "销售", s =>
        {
            s.WithOptions(o =>
            {
                o.Order = 100;
                o.Icon = "fa-dollar";
            });
            s.AddPermission("View", "查看");
        });
    }
}

// Registrar 2: HR domain
public class HrRegistrar : IPermissionRegistrar<TestGroupOptions, TestPermissionOptions>
{
    public void Register(PermissionBuilder<TestGroupOptions, TestPermissionOptions> builder)
    {
        builder.DefineGroup("HR", "人资", h =>
        {
            h.WithOptions(o => { o.Order = 200; });
            h.AddPermission("Edit", "编辑", o => { o.Critical = true; });
        });
    }
}

public class MultiRegistrarConsistencyTests
{
    [Fact]
    public void MultipleRegistrars_BuildCombinedTree_WithConsistentGenerics()
    {
        var appNs = typeof(MultiRegistrarConsistencyTests).Assembly.GetName().Name + ".Generated";
        var appPermissionsType = typeof(MultiRegistrarConsistencyTests).Assembly.GetType(appNs + ".AppPermissions");
        Assert.NotNull(appPermissionsType);

        // Roots should include Sales and HR
        var getAll = appPermissionsType.GetMethod("GetAllGroups");
        var roots = ((IEnumerable)getAll!.Invoke(null, null)!).Cast<object>().ToList();
        var rootNames = roots.Select(r => (string)r.GetType().GetProperty("LogicalName")!.GetValue(r)!).ToList();
        Assert.Contains("Sales", rootNames);
        Assert.Contains("HR", rootNames);

        // Lookup dictionaries are IReadOnlyDictionary
        var groupsByKeyField = appPermissionsType.GetField("GroupsByKey")!;
        var permsByKeyField = appPermissionsType.GetField("PermsByKey")!;
        Assert.Equal("IReadOnlyDictionary`2", groupsByKeyField.FieldType.GetGenericTypeDefinition().Name);
        Assert.Equal("IReadOnlyDictionary`2", permsByKeyField.FieldType.GetGenericTypeDefinition().Name);

        // Verify Sales group
        var groupsDict = (IDictionary)groupsByKeyField.GetValue(null)!;
        var sales = groupsDict["Sales"]!;
        var orderProp = sales.GetType().GetProperty("Order")!;
        var iconProp = sales.GetType().GetProperty("Icon")!;
        Assert.Equal(100, (int)orderProp.GetValue(sales)!);
        Assert.Equal("fa-dollar", (string?)iconProp.GetValue(sales));

        // Verify HR and its permission
        var hr = groupsDict["HR"]!;
        var childrenProp = hr.GetType().GetProperty("SubGroups")!;
        Assert.Equal("IReadOnlyList`1", childrenProp.PropertyType.GetGenericTypeDefinition().Name);
        var permsProp = hr.GetType().GetProperty("Permissions")!;
        Assert.Equal("IReadOnlyList`1", permsProp.PropertyType.GetGenericTypeDefinition().Name);
        var perms = ((IEnumerable)permsProp.GetValue(hr)!).Cast<object>().ToList();
        Assert.Single(perms);
        var edit = perms[0];
        var keyProp = edit.GetType().GetProperty("Key")!;
        var critProp = edit.GetType().GetProperty("Critical")!;
        Assert.Equal("HR_Edit", (string)keyProp.GetValue(edit)!);
        Assert.True((bool)critProp.GetValue(edit)!);

        // Keys constants present
        var keysType = appPermissionsType.GetNestedType("Keys")!;
        Assert.NotNull(keysType.GetField("Sales_View"));
        Assert.NotNull(keysType.GetField("HR_Edit"));
    }
}