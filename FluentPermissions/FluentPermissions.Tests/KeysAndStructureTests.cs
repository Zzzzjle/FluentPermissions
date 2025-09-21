using System;
using System.IO;
using System.Linq;
using Xunit;

namespace FluentPermissions.Tests;

public class KeysAndStructureTests
{
    [Fact]
    public void Generate_AppPermissions_From_Generic_Registrar()
    {
        const string sources = """

                               using FluentPermissions.Core.Abstractions;
                               using FluentPermissions.Core.Builder;

                               namespace DemoApp;

                               public class TestGroupOptions : PermissionOptionsBase
                               {
                                   public int Order { get; set; }
                                   public string? Icon { get; set; }
                               }

                               public class TestPermissionOptions : PermissionOptionsBase
                               {
                                   public bool Critical { get; set; }
                               }

                               public sealed class DemoRegistrar : IPermissionRegistrar<TestGroupOptions, TestPermissionOptions>
                               {
                                   public void Register(PermissionBuilder<TestGroupOptions, TestPermissionOptions> builder)
                                   {
                                       builder
                                           .DefineGroup("System", "系统", "核心系统设置", system =>
                                           {
                                               system.WithOptions(o => o.Order = 10);
                                               system.DefineGroup("Users", "用户账户管理", users =>
                                               {
                                                   users.AddPermission("Create", "创建用户");
                                               });
                                           })
                                           .DefineGroup("Reports", reports =>
                                           {
                                               reports.WithOptions(o => o.Order = 20);
                                               reports.AddPermission("View", "查看报表");
                                           });
                                   }
                               }

                               """;

        var compilation = TestCompilationHelper.CreateCompilation("GeneratorDriver_Generic_Test", sources);
        var driver = TestCompilationHelper.CreateDriver();

        var runResult = driver.RunGenerators(compilation).GetRunResult();

        Assert.True(runResult.Diagnostics.IsEmpty,
            string.Join(Environment.NewLine, runResult.Diagnostics.Select(d => d.ToString())));

        var files = runResult.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)).ToArray();
        Assert.Contains("AppPermissions.g.cs", files);
        Assert.Contains("FluentPermissions.g.Models.cs", files);

        var appTree = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("AppPermissions.g.cs"));
        var appText = appTree.GetText().ToString();

        Assert.Contains("namespace GeneratorDriver_Generic_Test.Auth;", appText);
        Assert.Contains("public static partial class AppPermissions", appText);
        Assert.Contains("public static class Keys", appText);
        Assert.Contains("public const string System = \"System\";", appText);
        // Reports 是顶层组（与 System 并列），不应为 System_Reports
        Assert.Contains("public const string Reports = \"Reports\";", appText);
        Assert.Contains("public static class System", appText);
        Assert.Contains("public static class Reports", appText);
    }

    [Fact]
    public void Generate_From_NonGeneric_Registrar()
    {
        const string sources = """

                               using FluentPermissions.Core.Abstractions;
                               using FluentPermissions.Core.Builder;

                               namespace DemoApp2;

                               public sealed class NonGenericRegistrar : IPermissionRegistrar
                               {
                                   public void Register(PermissionBuilder builder)
                                   {
                                       builder
                                           .DefineGroup("SystemNG", "系统(NG)", system =>
                                           {
                                               system.DefineGroup("Users", "用户管理", users =>
                                               {
                                                   users.AddPermission("Create", "创建用户");
                                               });
                                           })
                                           .DefineGroup("ReportsNG", reports =>
                                           {
                                               reports.AddPermission("View", "查看报表");
                                           });
                                   }
                               }

                               """;

        var compilation = TestCompilationHelper.CreateCompilation("GeneratorDriver_NonGeneric_Test", sources);
        var driver = TestCompilationHelper.CreateDriver();
        var runResult = driver.RunGenerators(compilation).GetRunResult();

        var appSyntax = runResult.GeneratedTrees.Single(t => t.FilePath.EndsWith("AppPermissions.g.cs"));
        var appText = appSyntax.GetText().ToString();

        Assert.Contains("namespace GeneratorDriver_NonGeneric_Test.Auth;", appText);
        Assert.Contains("public const string SystemNG = \"SystemNG\";", appText);
        // ReportsNG 是顶层组
        Assert.Contains("public const string ReportsNG = \"ReportsNG\";", appText);
    }
}