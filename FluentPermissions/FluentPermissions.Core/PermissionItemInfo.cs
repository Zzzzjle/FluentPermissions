#if false
namespace FluentPermissions.Core;

/// <summary>
/// 表示一个独立的权限项。这是一个部分类，其余部分由源生成器生成。
/// </summary>
public sealed partial class PermissionItemInfo
{
    public string Name { get; }
    public string GroupName { get; }
    public PermissionGroupInfo? Group { get; internal set; }

    private PermissionItemInfo(string name, string groupName)
    {
        Name = name;
        GroupName = groupName;
    }

    public static implicit operator string?(PermissionItemInfo permission) => permission?.Name;
    public override string ToString() => Name;
}
#endif