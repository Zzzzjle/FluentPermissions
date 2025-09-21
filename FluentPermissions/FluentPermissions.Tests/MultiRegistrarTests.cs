using System.Linq;
using Xunit;

namespace FluentPermissions.Tests;

public class MultiRegistrarTests
{
    [Fact]
    public void Multiple_Registrars_Combine_Tree()
    {
        const string sources = """

                               using FluentPermissions.Core.Abstractions;
                               using FluentPermissions.Core.Builder;

                               namespace DemoMulti;

                               public class G : PermissionOptionsBase { public int Order { get; set; } public string? Icon { get; set; } }
                               public class P : PermissionOptionsBase { public bool Critical { get; set; } }

                               public sealed class SalesReg : IPermissionRegistrar<G, P>
                               {
                                   public void Register(PermissionBuilder<G, P> builder)
                                   {
                                       builder.DefineGroup("Sales", s => { s.WithOptions(o => { o.Order = 100; o.Icon = "fa-dollar"; }); s.AddPermission("View", "View"); });
                                   }
                               }

                               public sealed class HrReg : IPermissionRegistrar<G, P>
                               {
                                   public void Register(PermissionBuilder<G, P> builder)
                                   {
                                       builder.DefineGroup("HR", h => { h.AddPermission("Edit", "Edit", o => o.Critical = true); });
                                   }
                               }

                               """;

        var compilation = TestCompilationHelper.CreateCompilation("GeneratorDriver_Multi_Test", sources);
        var driver = TestCompilationHelper.CreateDriver();
        var result = driver.RunGenerators(compilation).GetRunResult();

        var app = result.GeneratedTrees.Single(t => t.FilePath.EndsWith("AppPermissions.g.cs"));
        var appText = app.GetText().ToString();

        Assert.Contains("public const string Sales_View = \"Sales_View\";", appText);
        Assert.Contains("public const string HR_Edit = \"HR_Edit\";", appText);
        Assert.Contains("fa-dollar", appText);
    }
}