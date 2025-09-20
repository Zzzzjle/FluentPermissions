using FluentPermissions.Core.Abstractions;
using FluentPermissions.Core.Builder;

namespace FluentPermissions.Sample.Definitions;

public class AppPermissionDefinition : IPermissionRegistrar<SampleGroupOptions, SamplePermissionOptions>
{
    public void Register(PermissionBuilder<SampleGroupOptions, SamplePermissionOptions> builder)
    {
        // @formatter:off
        builder
            .DefineGroup("System", "系统", "核心系统设置", o =>
            {
                o.Icon = "fa-gear";
                o.DisplayOrder = 10;
            })
                .DefineGroup("Users", "用户账户管理", o =>
                {
                    o.DisplayOrder = 1;
                })
                    .AddPermission("Create", "创建用户")
                    .AddPermission("Delete", "删除用户", "这是一个高风险操作", o =>
                    {
                        o.IsHighRisk = true;
                    })
                .Then()
                .DefineGroup("Roles", "角色管理", o =>
                {
                    o.DisplayOrder = 2;
                })
                    .AddPermission("Create", "创建角色")
                    .AddPermission("Assign", "分配角色")
                .Then()
            .Then()
            .DefineGroup("Reports", "报表中心", o =>
            {
                o.Icon = "fa-chart";
                o.DisplayOrder = 20;
            })
                .AddPermission("View", "查看报表")
                .AddPermission("Export", "导出报表");
    }
}