using FluentPermissions.Core.Abstractions;
using FluentPermissions.Core.Builder;

namespace FluentPermissions.Sample.Definitions;

public class AppPermissionDefinition : IPermissionRegistrar<SampleGroupOptions, SamplePermissionOptions>
{
    public void Register(PermissionBuilder<SampleGroupOptions, SamplePermissionOptions> builder)
    {
        builder
            .DefineGroup("System", o =>
            {
                o.Description = "系统";
                o.Icon = "fa-gear";
                o.DisplayOrder = 10;
            })
            .DefineGroup("Users", o =>
            {
                o.Description = "用户";
                o.DisplayOrder = 1;
            })
            .AddPermission("Create", o => { o.Description = "创建用户"; })
            .AddPermission("Delete", o =>
            {
                o.Description = "删除用户";
                o.IsHighRisk = true;
            })
            .Then()
            .DefineGroup("Roles", o =>
            {
                o.Description = "角色";
                o.DisplayOrder = 2;
            })
            .AddPermission("Create", o => { o.Description = "创建角色"; })
            .AddPermission("Assign", o => { o.Description = "分配角色"; })
            .Then()
            .Then()
            .DefineGroup("Reports", o =>
            {
                o.Description = "报表";
                o.Icon = "fa-chart";
                o.DisplayOrder = 20;
            })
            .AddPermission("View", o => { o.Description = "查看报表"; })
            .AddPermission("Export", o => { o.Description = "导出报表"; });
    }
}