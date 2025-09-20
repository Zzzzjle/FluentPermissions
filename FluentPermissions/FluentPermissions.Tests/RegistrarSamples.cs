using FluentPermissions.Core.Abstractions;
using FluentPermissions.Core.Builder;
using Xunit;
using System.Linq;

namespace FluentPermissions.Tests;

public class TestGroupOptions : PermissionOptionsBase
{
    public int Order { get; set; }
}

public class TestPermissionOptions : PermissionOptionsBase
{
    public bool Critical { get; set; }
}

public class TestRegistrar : IPermissionRegistrar<TestGroupOptions, TestPermissionOptions>
{
    public void Register(PermissionBuilder<TestGroupOptions, TestPermissionOptions> builder)
    {
        builder
            .DefineGroup("G1", "Group One", "Group1 Desc", o => { o.Order = 7; })
                .AddPermission("P1", "Perm1")
                .AddPermission("P2", "Perm2", null, o => { o.Critical = true; });
    }
}

public class GeneratorUsageTests
{
    [Fact]
    public void ModelPropertiesAndNavigation()
    {
        // 引用由生成器输出的命名空间
        // 运行时测试中，我们直接使用当前程序集的 Generated 命名空间
        var appNs = typeof(GeneratorUsageTests).Assembly.GetName().Name + ".Generated";
        var appPermissionsType = typeof(GeneratorUsageTests).Assembly.GetType(appNs + ".AppPermissions");
        Assert.NotNull(appPermissionsType);

    // 通过嵌套访问器获取组实例
    var nestedG1 = appPermissionsType!.GetNestedType("G1");
    Assert.NotNull(nestedG1);
    var nestedGroupField = nestedG1!.GetField("Group");
    Assert.NotNull(nestedGroupField);
    var group = nestedGroupField!.GetValue(null)!;

    // 反射类型: PermissionGroupInfo
        var groupType = group.GetType();
        var logicalNameProp = groupType.GetProperty("LogicalName")!;
        var displayNameProp = groupType.GetProperty("DisplayName")!;
        var descProp = groupType.GetProperty("Description")!; // top-level
        var orderProp = groupType.GetProperty("Order")!;
        var permsProp = groupType.GetProperty("Permissions")!;

        Assert.Equal("G1", logicalNameProp.GetValue(group));
        Assert.Equal("Group One", displayNameProp.GetValue(group));
        Assert.Equal("Group1 Desc", descProp.GetValue(group));
        Assert.Equal(7, orderProp.GetValue(group));

        var perms = (System.Collections.IEnumerable)permsProp.GetValue(group)!;
        var list = perms.Cast<object>().ToList();
        Assert.Equal(2, list.Count);

        var p1 = list[0];
        var p2 = list[1];
        var pType = p1.GetType();
        var pLogical = pType.GetProperty("LogicalName")!;
        var pDisplay = pType.GetProperty("DisplayName")!;
        var pDesc = pType.GetProperty("Description")!;
        var pCritical = pType.GetProperty("Critical")!;
        var pGroupKey = pType.GetProperty("GroupKey")!;

        Assert.Equal("P1", pLogical.GetValue(p1));
        Assert.Equal("Perm1", pDisplay.GetValue(p1));
        Assert.Null(pDesc.GetValue(p1));
        Assert.False((bool)pCritical.GetValue(p1)!);
        Assert.Equal("G1", pGroupKey.GetValue(p1));

        Assert.Equal("P2", pLogical.GetValue(p2));
        Assert.Equal("Perm2", pDisplay.GetValue(p2));
        Assert.Null(pDesc.GetValue(p2));
        Assert.True((bool)pCritical.GetValue(p2)!);

        var p1Field = nestedG1.GetField("P1");
        var p2Field = nestedG1.GetField("P2");
        Assert.NotNull(p1Field);
        Assert.NotNull(p2Field);

        // 验证全局查找字典
        var groupsByKeyField = appPermissionsType.GetField("GroupsByKey");
        Assert.NotNull(groupsByKeyField);
        var groupsDict = (System.Collections.IDictionary)groupsByKeyField!.GetValue(null)!;
        Assert.True(groupsDict.Contains("G1"));
    }
}
