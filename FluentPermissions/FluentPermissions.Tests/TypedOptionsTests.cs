using System.Linq;
using Xunit;

namespace FluentPermissions.Tests;

public class TypedOptionsTests
{
    [Fact]
    public void Group_And_Permission_Typed_Props_Appear_In_Models()
    {
        const string sources = """

                               using FluentPermissions.Core.Abstractions;
                               using FluentPermissions.Core.Builder;

                               namespace DemoAppOpts;

                               public class TestGroupOptions : PermissionOptionsBase
                               {
                                   public int Order { get; set; }
                                   public string? Icon { get; set; }
                               }

                               public class TestPermissionOptions : PermissionOptionsBase
                               {
                                   public bool Critical { get; set; }
                               }

                               public sealed class Registrar : IPermissionRegistrar<TestGroupOptions, TestPermissionOptions>
                               {
                                   public void Register(PermissionBuilder<TestGroupOptions, TestPermissionOptions> builder)
                                   {
                                       builder
                                           .DefineGroup("System", "系统", g =>
                                           {
                                               g.WithOptions(o => { o.Order = 7; o.Icon = "fa-gear"; });
                                               g.AddPermission("P1", "Perm1", o => { o.Critical = true; });
                                           });
                                   }
                               }

                               """;

        var compilation = TestCompilationHelper.CreateCompilation("GeneratorDriver_TypedOpts_Test", sources);
        var driver = TestCompilationHelper.CreateDriver();
        var result = driver.RunGenerators(compilation).GetRunResult();

        var models = result.GeneratedTrees.Single(t => t.FilePath.EndsWith("FluentPermissions.g.Models.cs"));
        var text = models.GetText().ToString();

        Assert.Contains("public int Order { get; }", text);
        Assert.Contains("public string? Icon { get; }", text);
        Assert.Contains("public bool Critical { get; }", text);
    }
}
