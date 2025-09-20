using FluentPermissions.Core.Abstractions;
using FluentPermissions.Core.Builder;
using Xunit;
using System.Linq;

namespace FluentPermissions.Tests;

public class DefaultsRegistrar : IPermissionRegistrar<TestGroupOptions, TestPermissionOptions>
{
    public void Register(PermissionBuilder<TestGroupOptions, TestPermissionOptions> builder)
    {
        // No WithOptions at group level, and no options for permission either
        builder.DefineGroup("Defaults", "默认", g =>
        {
            g.AddPermission("None", "无");
        });
    }
}

public class DefaultOptionsTests
{
    [Fact]
    public void Options_Defaults_Are_Applied_When_Not_Specified()
    {
        var appNs = typeof(DefaultOptionsTests).Assembly.GetName().Name + ".Generated";
        var appPermissionsType = typeof(DefaultOptionsTests).Assembly.GetType(appNs + ".AppPermissions");
        Assert.NotNull(appPermissionsType);

        var groupsByKeyField = appPermissionsType!.GetField("GroupsByKey")!;
        var dict = (System.Collections.IDictionary)groupsByKeyField.GetValue(null)!;
        var g = dict["Defaults"]!;
        var orderProp = g.GetType().GetProperty("Order")!;
        var iconProp = g.GetType().GetProperty("Icon")!;
        Assert.Equal(0, (int)orderProp.GetValue(g)!);
        Assert.Null(iconProp.GetValue(g));

        var permsProp = g.GetType().GetProperty("Permissions")!;
        var perms = ((System.Collections.IEnumerable)permsProp.GetValue(g)!).Cast<object>().ToList();
        Assert.Single(perms);
        var p = perms[0];
        var criticalProp = p.GetType().GetProperty("Critical")!;
        Assert.False((bool)criticalProp.GetValue(p)!);
    }
}
