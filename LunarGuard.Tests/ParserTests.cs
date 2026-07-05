using LunarGuard.Core.Syntax;
using Xunit;

namespace LunarGuard.Tests;

public class ParserTests
{
    [Fact]
    public void Parse_SimpleIf_ShouldSucceed()
    {
        var src = "local x = 42\nif x > 10 then\nprint(\"big\")\nend";
        var lexer = new Lexer(src);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ex = Record.Exception(() => parser.Parse());
        Assert.Null(ex);
    }

    [Fact]
    public void Parse_FunctionWithColon_ShouldSucceed()
    {
        var src = "function player:takeDamage(amount)\nself.health = self.health - amount\nend";
        var lexer = new Lexer(src);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ex = Record.Exception(() => parser.Parse());
        Assert.Null(ex);
    }

    [Fact]
    public void Parse_FullScript_ShouldSucceed()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "test_script.lua");
        var src = File.ReadAllText(path);
        var lexer = new Lexer(src);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ex = Record.Exception(() => parser.Parse());
        Assert.Null(ex);
    }
}
