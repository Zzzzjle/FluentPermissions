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
            .DefineGroup("G1", o => { o.Description = "Group1"; o.Order = 7; })
                .AddPermission("P1", o => { o.Description = "Perm1"; })
                .AddPermission("P2", o => { o.Description = "Perm2"; o.Critical = true; });
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

        // 静态字段: G1Group
        var g1GroupField = appPermissionsType!.GetField("G1Group");
        Assert.NotNull(g1GroupField);
        var group = g1GroupField!.GetValue(null)!;

        // 反射类型: PermissionGroupInfo 位于 FluentPermissions.Core 命名空间（生成器输出）
        var groupType = group.GetType();
        var nameProp = groupType.GetProperty("Name")!;
        var descProp = groupType.GetProperty("Description")!; // from base options
        var orderProp = groupType.GetProperty("Order")!;
        var permsProp = groupType.GetProperty("Permissions")!;

        Assert.Equal("G1", nameProp.GetValue(group));
        Assert.Equal("Group1", descProp.GetValue(group));
        Assert.Equal(7, orderProp.GetValue(group));

        var perms = (System.Collections.IEnumerable)permsProp.GetValue(group)!;
        var list = perms.Cast<object>().ToList();
        Assert.Equal(2, list.Count);

        var p1 = list[0];
        var p2 = list[1];
        var pType = p1.GetType();
        var pName = pType.GetProperty("Name")!;
        var pDesc = pType.GetProperty("Description")!;
        var pCritical = pType.GetProperty("Critical")!;
        var pGroup = pType.GetProperty("Group")!;
        var pGroupName = pType.GetProperty("GroupName")!;

        Assert.Equal("P1", pName.GetValue(p1));
        Assert.Equal("Perm1", pDesc.GetValue(p1));
        Assert.False((bool)pCritical.GetValue(p1)!);
        Assert.Equal("G1", pGroupName.GetValue(p1));
        Assert.Same(group, pGroup.GetValue(p1));

        Assert.Equal("P2", pName.GetValue(p2));
        Assert.Equal("Perm2", pDesc.GetValue(p2));
        Assert.True((bool)pCritical.GetValue(p2)!);
        Assert.Equal("G1", pGroupName.GetValue(p2));
        Assert.Same(group, pGroup.GetValue(p2));

        // 嵌套访问器类: AppPermissions.G1
        var nestedG1 = appPermissionsType.GetNestedType("G1");
        Assert.NotNull(nestedG1);
        var nestedGroupField = nestedG1!.GetField("Group");
        Assert.NotNull(nestedGroupField);
        Assert.Same(group, nestedGroupField!.GetValue(null));

        var p1Field = nestedG1.GetField("P1");
        var p2Field = nestedG1.GetField("P2");
        Assert.NotNull(p1Field);
        Assert.NotNull(p2Field);
    }
}
