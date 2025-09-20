using FluentPermissions.Core.Abstractions;
using FluentPermissions.Core.Builder;

namespace FluentPermissions.Sample.Definitions;

public class AppPermissionDefinition : IPermissionRegistrar<SampleGroupOptions, SamplePermissionOptions>
{
    public void Register(PermissionBuilder<SampleGroupOptions, SamplePermissionOptions> builder)
    {
        builder
            .DefineGroup("System", "系统", "核心系统设置", system =>
            {
                system.WithOptions(options =>
                {
                    options.Icon = "fa-gear";
                    options.DisplayOrder = 10;
                });

                system.DefineGroup("Users", "用户账户管理", users =>
                {
                    users.AddPermission("Create", "创建用户");
                    users.AddPermission("Delete", "删除用户", "这是一个高风险操作", o => { o.IsHighRisk = true; });
                });

                system.DefineGroup("Roles", "角色管理", roles =>
                {
                    roles.AddPermission("Create", "创建角色");
                    roles.AddPermission("Assign", "分配角色");
                });
            })
            .DefineGroup("Reports", reports =>
            {
                reports.WithOptions(options =>
                {
                    options.Icon = "fa-chart";
                    options.DisplayOrder = 20;
                });
                reports.AddPermission("View", "查看报表");
                reports.AddPermission("Export", "导出报表");
            });
    }
}