using FluentPermissions.Core.Abstractions;
using FluentPermissions.Core.Builder;
using Xunit;

namespace FluentPermissions.Tests;

public class DeepRegistrar : IPermissionRegistrar<TestGroupOptions, TestPermissionOptions>
{
    public void Register(PermissionBuilder<TestGroupOptions, TestPermissionOptions> builder)
    {
        // Chain 1: A -> A1 -> A1a (permission X)
        builder.DefineGroup("A", "组A",
            a =>
            {
                a.DefineGroup("A1", "A1组",
                    a1 => { a1.DefineGroup("A1a", "A1a组", a1a => { a1a.AddPermission("X", "操作X"); }); });
            });

        // Chain 2: B (permission Y)
        builder.DefineGroup("B", "组B", b => { b.AddPermission("Y", "操作Y"); });
    }
}

public class DeepNestingTests
{
    [Fact]
    public void ParentKey_And_GroupKey_Are_Computed()
    {
        var appNs = typeof(DeepNestingTests).Assembly.GetName().Name + ".Generated";
        var appPermissionsType = typeof(DeepNestingTests).Assembly.GetType(appNs + ".AppPermissions");
        Assert.NotNull(appPermissionsType);

        // Access nested classes: AppPermissions.A, A.A1, A.A1a
        var aClass = appPermissionsType.GetNestedType("A");
        Assert.NotNull(aClass);
        var aGroup = aClass.GetField("Group")!.GetValue(null)!;
        var aType = aGroup.GetType();
        var keyProp = aType.GetProperty("Key")!;
        var parentKeyProp = aType.GetProperty("ParentKey")!;
    Assert.Null(parentKeyProp.GetValue(aGroup)); // root has no parent
    Assert.Equal("A", keyProp.GetValue(aGroup));

        var a1Class = aClass.GetNestedType("A1");
        Assert.NotNull(a1Class);
        var a1Group = a1Class.GetField("Group")!.GetValue(null)!;
    Assert.Equal("A_A1", keyProp.GetValue(a1Group));
    Assert.Equal("A", parentKeyProp.GetValue(a1Group));

        var a1AClass = a1Class.GetNestedType("A1a");
        Assert.NotNull(a1AClass);
        var a1AGroup = a1AClass.GetField("Group")!.GetValue(null)!;
    Assert.Equal("A_A1_A1a", keyProp.GetValue(a1AGroup));
    Assert.Equal("A_A1", parentKeyProp.GetValue(a1AGroup));

        // Permission X under A.A1.A1a
        var xField = a1AClass.GetField("X");
        Assert.NotNull(xField);
        var xPerm = xField.GetValue(null)!;
        var pType = xPerm.GetType();
        var pGroupKey = pType.GetProperty("GroupKey")!;
        var pKey = pType.GetProperty("Key")!;
    Assert.Equal("A_A1_A1a", pGroupKey.GetValue(xPerm));
    Assert.Equal("A_A1_A1a_X", pKey.GetValue(xPerm));

        // Keys constants exist
        var keysClass = appPermissionsType.GetNestedType("Keys");
        Assert.NotNull(keysClass);
        var constName = "A_A1_A1a_X";
    var kField = keysClass.GetField(constName);
    Assert.NotNull(kField);
    Assert.Equal("A_A1_A1a_X", (string)kField.GetValue(null)!);

        // Second chain: B and permission Y
        var bClass = appPermissionsType.GetNestedType("B");
        Assert.NotNull(bClass);
        var bGroup = bClass.GetField("Group")!.GetValue(null)!;
    Assert.Equal("B", keyProp.GetValue(bGroup));
        Assert.Null(parentKeyProp.GetValue(bGroup));

        var yField = bClass.GetField("Y");
        Assert.NotNull(yField);
        var yPerm = yField.GetValue(null)!;
    Assert.Equal("B_Y", pKey.GetValue(yPerm));

        // GroupsByKey contains all
        var groupsByKeyField = appPermissionsType.GetField("GroupsByKey");
        Assert.NotNull(groupsByKeyField);
        var dict = (System.Collections.IDictionary)groupsByKeyField.GetValue(null)!;
    Assert.True(dict.Contains("A"));
    Assert.True(dict.Contains("A_A1"));
    Assert.True(dict.Contains("A_A1_A1a"));
    Assert.True(dict.Contains("B"));
    }
}