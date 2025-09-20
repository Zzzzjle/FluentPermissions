#if false
using System.Collections.Generic;

namespace FluentPermissions.Core;

/// <summary>
/// 表示一个权限组。这是一个部分类，其余部分由源生成器生成。
/// </summary>
public sealed partial class PermissionGroupInfo
{
    public string Name { get; }
    public IReadOnlyList<PermissionItemInfo> Permissions { get; internal set; }

    private PermissionGroupInfo(string name)
    {
        Name = name;
        Permissions = new List<PermissionItemInfo>();
    }
}
#endif