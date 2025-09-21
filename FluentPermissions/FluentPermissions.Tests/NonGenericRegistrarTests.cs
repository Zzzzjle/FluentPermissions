using System.Collections;
using System.Linq;
using FluentPermissions.Core.Abstractions;
using FluentPermissions.Core.Builder;
using Xunit;

namespace FluentPermissions.Tests;

public sealed class NonGenericRegistrarTests
{
    [Fact]
    public void NonGeneric_NoOptions_GeneratesTreeWithUnderscoreKeys()
    {
        var appNs = typeof(NonGenericRegistrarTests).Assembly.GetName().Name + ".Generated";
        var appPermissionsType = typeof(NonGenericRegistrarTests).Assembly.GetType(appNs + ".AppPermissions");
        Assert.NotNull(appPermissionsType);

        var getAll = appPermissionsType!.GetMethod("GetAllGroups");
        Assert.NotNull(getAll);
        var roots = ((IEnumerable)getAll!.Invoke(null, null)!).Cast<object>().ToList();
        Assert.True(roots.Count >= 2);

        var keys = appPermissionsType.GetNestedType("Keys");
        Assert.NotNull(keys);
        Assert.NotNull(keys!.GetField("SystemNG_Users_Create"));
        Assert.NotNull(keys.GetField("SystemNG_Roles_Assign"));
        Assert.NotNull(keys.GetField("ReportsNG_View"));

        var groupsByKey = appPermissionsType.GetField("GroupsByKey")!.GetValue(null)!;
        var permsByKey = appPermissionsType.GetField("PermsByKey")!.GetValue(null)!;
        Assert.NotNull(groupsByKey);
        Assert.NotNull(permsByKey);
    }

    private sealed class NonGenericAppPermissionDefinition : IPermissionRegistrar
    {
        public void Register(PermissionBuilder builder)
        {
            builder
                .DefineGroup("SystemNG", "系统(NG)", "核心系统设置", system =>
                {
                    system.DefineGroup("Users", "用户账户管理", users =>
                    {
                        users.AddPermission("Create", "创建用户");
                        users.AddPermission("Delete", "删除用户", "高风险删除");
                    });

                    system.DefineGroup("Roles", "角色管理", roles => { roles.AddPermission("Assign", "分配角色"); });
                })
                .DefineGroup("ReportsNG", "报表(NG)", reports => { reports.AddPermission("View", "查看报表"); });
        }
    }
}