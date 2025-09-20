using System;
using FluentPermissions.Sample.Generated;

namespace FluentPermissions.Sample;

public static class Program
{
    public static void Main()
    {
        // 访问生成的层级结构：System -> Users -> Create/Delete
        var system = AppPermissions.System.Group;
        Console.WriteLine(
            $"Group: {system.LogicalName}, Display: {system.DisplayName}, FullName: {system.FullName}, Key: {system.Key}, Icon: {system.Icon}, Order: {system.DisplayOrder}, Desc: {system.Description}");

        var users = AppPermissions.System.Users.Group;
        Console.WriteLine(
            $"Child Group: {users.LogicalName}, Display: {users.DisplayName}, FullName: {users.FullName}, Key: {users.Key}");

        var create = AppPermissions.System.Users.Create;
        var delete = AppPermissions.System.Users.Delete;
        Console.WriteLine(
            $"Perm: {create.FullName}, Display: {create.DisplayName}, Key: {create.Key}, Desc: {create.Description}");
        Console.WriteLine(
            $"Perm: {delete.FullName}, Display: {delete.DisplayName}, Key: {delete.Key}, Desc: {delete.Description}, HighRisk: {delete.IsHighRisk}");

        // Keys 常量访问（全局扁平映射）
        Console.WriteLine("Keys mapping:");
        Console.WriteLine($"System_Users_Create => {AppPermissions.Keys.System_Users_Create}");
        Console.WriteLine($"System_Users_Delete => {AppPermissions.Keys.System_Users_Delete}");

        // 导航关系
        Console.WriteLine(
            $"Users.GroupKey == System.Key? {string.Equals(users.Key, system.Key, StringComparison.Ordinal)}");
        Console.WriteLine($"Create.GroupKey -> {create.GroupKey}");
        var parentGroup = AppPermissions.GroupsByKey[create.GroupKey];
        Console.WriteLine($"Parent Group from GroupsByKey: {parentGroup.DisplayName}");
        Console.WriteLine($"System.Children.Count: {system.Children.Count}");
        Console.WriteLine($"Users.Permissions.Count: {users.Permissions.Count}");

        Console.WriteLine("All root groups:");
        foreach (var g in AppPermissions.GetAllGroups())
        {
            PermissionModels.RegistrarData.Build(); // Ensure built
            Console.WriteLine($"- {g.FullName} ({g.Permissions.Count} perms, {g.Children.Count} children)");
        }
    }
}

public class TestAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}