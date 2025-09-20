using FluentPermissions.Core.Abstractions;
using FluentPermissions.Core.Builder;

namespace FluentPermissions.Sample.Definitions;

public class AppPermissionDefinition : IPermissionRegistrar<SampleGroupOptions, SamplePermissionOptions>
{
    public void Register(PermissionBuilder<SampleGroupOptions, SamplePermissionOptions> builder)
    {
        builder
            .DefineGroup("Management", options =>
            {
                options.Description = "后台管理";
                options.Icon = "fa-cogs";
                options.DisplayOrder = 100;
            })
                .AddPermission("Users", options =>
                {
                    options.Description = "管理用户";
                })
                .AddPermission("Settings", options =>
                {
                    options.Description = "管理系统设置";
                    options.IsHighRisk = true;
                });
    }
}