using System;
using FluentPermissions.Sample.Generated;

namespace FluentPermissions.Sample;

public static class Program
{
    public static void Main()
    {
        // 访问生成的权限组与权限项
        var group = AppPermissions.Management.Group;
        Console.WriteLine($"Group: {group.Name}, Icon: {group.Icon}, DisplayOrder: {group.DisplayOrder}, Description: {group.Description}");

        var users = AppPermissions.Management.Users;
        Console.WriteLine($"Permission: {users.Name}, Group: {users.GroupName}, IsHighRisk: {users.IsHighRisk}, Description: {users.Description}");

        var settings = AppPermissions.Management.Settings;
        Console.WriteLine($"Permission: {settings.Name}, Group: {settings.GroupName}, IsHighRisk: {settings.IsHighRisk}, Description: {settings.Description}");

        // 导航关系校验
        Console.WriteLine($"Users.Group == group? {ReferenceEquals(users.Group, group)}");
        Console.WriteLine($"Settings.Group == group? {ReferenceEquals(settings.Group, group)}");
        Console.WriteLine($"Group.Permissions.Count: {group.Permissions.Count}");

        Console.WriteLine("All groups:");
        foreach (var g in AppPermissions.GetAllGroups())
        {
            Console.WriteLine($"- {g.Name} ({g.Permissions.Count} perms)");
        }
    }
}