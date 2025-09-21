using System.Linq;
using Xunit;

namespace FluentPermissions.Tests;

public class DeepNestingDriverTests
{
    [Fact]
    public void Deep_Nesting_Generates_Keys_And_Classes()
    {
        const string sources = """

                               using FluentPermissions.Core.Abstractions;
                               using FluentPermissions.Core.Builder;

                               namespace DemoDeep;

                               public class G : PermissionOptionsBase { }
                               public class P : PermissionOptionsBase { }

                               public sealed class Registrar : IPermissionRegistrar<G, P>
                               {
                                   public void Register(PermissionBuilder<G, P> builder)
                                   {
                                       builder
                                           .DefineGroup("A", a =>
                                           {
                                               a.DefineGroup("A1", a1 =>
                                               {
                                                   a1.DefineGroup("A1a", aa =>
                                                   {
                                                       aa.AddPermission("X", "X");
                                                   });
                                               });
                                           })
                                           .DefineGroup("B", b => { b.AddPermission("Y", "Y" ); });
                                   }
                               }

                               """;

        var compilation = TestCompilationHelper.CreateCompilation("GeneratorDriver_Deep_Test", sources);
        var driver = TestCompilationHelper.CreateDriver();
        var result = driver.RunGenerators(compilation).GetRunResult();

        var app = result.GeneratedTrees.Single(t => t.FilePath.EndsWith("AppPermissions.g.cs"));
        var appText = app.GetText().ToString();

        Assert.Contains("public const string A_A1_A1a_X = \"A_A1_A1a_X\";", appText);
        Assert.Contains("public const string B_Y = \"B_Y\";", appText);
        Assert.Contains("public static class A", appText);
        Assert.Contains("public static class A1", appText);
        Assert.Contains("public static class A1a", appText);
    }
}