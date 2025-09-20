using FluentPermissions.Core.Abstractions;

namespace FluentPermissions.Sample.Definitions;

public class SampleGroupOptions : PermissionOptionsBase
{
    public int DisplayOrder { get; set; }
    public string? Icon { get; set; }
}

public class SamplePermissionOptions : PermissionOptionsBase
{
    public bool IsHighRisk { get; set; }
}