using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace FluentPermissions.Tests;

public class DiagnosticsTests
{
    [Fact]
    public void Inconsistent_Generic_Options_Produces_Error_Diagnostic()
    {
        const string sources = """

                               using FluentPermissions.Core.Abstractions;
                               using FluentPermissions.Core.Builder;

                               namespace DemoDiag;

                               public class G1 : PermissionOptionsBase { }
                               public class P1 : PermissionOptionsBase { }
                               public class G2 : PermissionOptionsBase { }
                               public class P2 : PermissionOptionsBase { }

                               public sealed class Reg1 : IPermissionRegistrar<G1, P1>
                               {
                                   public void Register(PermissionBuilder<G1, P1> builder)
                                   {
                                       builder.DefineGroup("A", _ => { });
                                   }
                               }

                               public sealed class Reg2 : IPermissionRegistrar<G2, P2>
                               {
                                   public void Register(PermissionBuilder<G2, P2> builder)
                                   {
                                       builder.DefineGroup("B", _ => { });
                                   }
                               }

                               """;

        var compilation = TestCompilationHelper.CreateCompilation("GeneratorDriver_Diag_Inconsistent_Test", sources);
        var driver = TestCompilationHelper.CreateDriver();
        var result = driver.RunGenerators(compilation).GetRunResult();

        var diag = result.Diagnostics.FirstOrDefault(d => d.Id == "FP0002");
        Assert.NotNull(diag);
        Assert.Equal(DiagnosticSeverity.Error, diag!.Severity);
    }

    [Fact]
    public void Missing_Register_Method_Produces_Warning_Diagnostic()
    {
        const string sources = """

                               using FluentPermissions.Core.Abstractions;

                               namespace DemoWarn;

                               public class G : PermissionOptionsBase { }
                               public class P : PermissionOptionsBase { }

                               public sealed class BadRegistrar : IPermissionRegistrar<G, P>
                               {
                                   // Intentionally missing Register method
                               }

                               """;

        var compilation = TestCompilationHelper.CreateCompilation("GeneratorDriver_Diag_MissingRegister_Test", sources);
        var driver = TestCompilationHelper.CreateDriver();
        var result = driver.RunGenerators(compilation).GetRunResult();

        var diag = result.Diagnostics.FirstOrDefault(d => d.Id == "FP0001");
        Assert.NotNull(diag);
        Assert.Equal(DiagnosticSeverity.Warning, diag!.Severity);
    }
}
