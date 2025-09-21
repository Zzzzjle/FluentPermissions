using Xunit;

namespace FluentPermissions.Tests;

public class NoRegistrarTests
{
    [Fact]
    public void No_Registrars_Emits_No_Files()
    {
        const string sources = """

                               namespace Empty;
                               public class C { }

                               """;
        var compilation = TestCompilationHelper.CreateCompilation("GeneratorDriver_Empty_Test", sources);
        var driver = TestCompilationHelper.CreateDriver();
        var result = driver.RunGenerators(compilation).GetRunResult();

        Assert.Empty(result.GeneratedTrees);
        Assert.Empty(result.Diagnostics);
    }
}
