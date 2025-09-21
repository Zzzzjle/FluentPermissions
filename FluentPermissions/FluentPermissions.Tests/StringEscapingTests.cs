using System.Linq;
using Xunit;

namespace FluentPermissions.Tests;

public class StringEscapingTests
{
    [Fact]
    public void String_Escaping_Is_Handled()
    {
        const string sources = """"

                               using FluentPermissions.Core.Abstractions;
                               using FluentPermissions.Core.Builder;

                               namespace DemoEscape;

                               public class G : PermissionOptionsBase { }
                               public class P : PermissionOptionsBase { }

                               public sealed class EscReg : IPermissionRegistrar<G, P>
                               {
                                   public void Register(PermissionBuilder<G, P> builder)
                                   {
                                       builder.DefineGroup("Esc", @"He said: ""Hi""", _ => { });
                                       // 包含换行符的显示名，验证 \n 转义
                                       builder.DefineGroup("NL", "Line1\nLine2", _ => { });
                                       builder.DefineGroup("Path", @"C:\\temp\\file", _ => { });
                                   }
                               }

                               """";

        var compilation = TestCompilationHelper.CreateCompilation("GeneratorDriver_Escape_Test", sources);
        var driver = TestCompilationHelper.CreateDriver();
        var result = driver.RunGenerators(compilation).GetRunResult();

        var app = result.GeneratedTrees.Single(t => t.FilePath.EndsWith("AppPermissions.g.cs"));
        var appText = app.GetText().ToString();

        Assert.Contains("\\\"Hi\\\"", appText); // 引号转义
        Assert.Contains("\\n", appText); // 换行符转义（来自组显示名）
        Assert.Contains("""Path", "Path", "C:\\""", appText); // 反斜杠转义（前缀片段，避免结尾歧义）
    }
}
