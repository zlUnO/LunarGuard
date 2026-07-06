using LunarGuard.Core;
using LunarGuard.Core.AST;
using LunarGuard.Core.AST.Stmt;
using LunarGuard.Core.AST.Expr;
using LunarGuard.Core.CodeGen;
using LunarGuard.Core.Obfuscation;
using LunarGuard.Core.Obfuscation.Virtualization;
using LunarGuard.Core.Syntax;

namespace LunarGuard.Tests;

public class ObfuscationPassTests
{
    private static BlockStmt Parse(string src)
    {
        var lexer = new Lexer(src);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var stmts = parser.Parse();
        var root = new BlockStmt();
        root.Statements.AddRange(stmts);
        return root;
    }

    private static string Write(AstNode node)
    {
        var writer = new LuaWriter();
        return writer.Write(node);
    }

    private static ObfuscationOptions AllDisabled() => new()
    {
        RenameVariables = false,
        EncryptStrings = false,
        EncodeNumbers = false,
        InjectDeadCode = false,
        ObfuscateControlFlow = false,
        SplitExpressions = false,
        AntiDebug = false,
        Virtualize = false
    };

    // ── RenamePass ─────────────────────────────────────────────────

    [Fact]
    public void RenamePass_RenamesLocalVars()
    {
        var root = Parse("local x = 1; local y = x + 1");
        var pass = new RenamePass();
        var opts = AllDisabled();
        opts.RenameVariables = true;
        pass.Transform(root, opts);

        var code = Write(root);
        Assert.DoesNotContain("local x", code);
        Assert.DoesNotContain("local y", code);
        Assert.Contains("local _", code);
    }

    [Fact]
    public void RenamePass_RenamesFunctionParams()
    {
        var root = Parse("function foo(a, b) return a + b end");
        var pass = new RenamePass();
        var opts = AllDisabled();
        opts.RenameVariables = true;
        pass.Transform(root, opts);

        var code = Write(root);
        // Function name and params are all renamed
        Assert.DoesNotContain("(a, b)", code);
        Assert.DoesNotContain("return a + b", code);
        Assert.Contains("function", code);
    }

    [Fact]
    public void RenamePass_DoesNotRenameGlobals()
    {
        var root = Parse("print(\"hello\")");
        var pass = new RenamePass();
        var opts = AllDisabled();
        opts.RenameVariables = true;
        pass.Transform(root, opts);

        var code = Write(root);
        Assert.Contains("print", code); // globals starting w/ uppercase are preserved
    }

    [Fact]
    public void RenamePass_RenamesLocalEvenIfReserved()
    {
        // "self" is a reserved name, but as a local variable it still gets renamed
        var root = Parse("local self = 1");
        var pass = new RenamePass();
        var opts = AllDisabled();
        opts.RenameVariables = true;
        pass.Transform(root, opts);

        var code = Write(root);
        Assert.DoesNotContain("local self", code);
        Assert.Contains("local _", code);
    }

    [Fact]
    public void RenamePass_Disabled_DoesNothing()
    {
        var root = Parse("local x = 1");
        var pass = new RenamePass();
        var opts = AllDisabled();
        pass.Transform(root, opts);

        var code = Write(root);
        Assert.Contains("local x", code);
    }

    // ── StringEncryptPass ───────────────────────────────────────────

    [Fact]
    public void StringEncryptPass_ReplacesStringLiterals()
    {
        var root = Parse("local x = \"hello\"");
        var pass = new StringEncryptPass();
        var opts = AllDisabled();
        opts.EncryptStrings = true;
        pass.Transform(root, opts);

        var code = Write(root);
        Assert.DoesNotContain("\"hello\"", code);
        Assert.Contains("__se_", code);
    }

    [Fact]
    public void StringEncryptPass_ReplacesStringInTable()
    {
        var root = Parse("local t = {[\"key\"] = \"value\"}");
        var pass = new StringEncryptPass();
        var opts = AllDisabled();
        opts.EncryptStrings = true;
        pass.Transform(root, opts);

        var code = Write(root);
        Assert.DoesNotContain("\"key\"", code);
        Assert.DoesNotContain("\"value\"", code);
        Assert.Contains("__se_", code);
    }

    [Fact]
    public void StringEncryptPass_ReplacesStringInConcat()
    {
        var root = Parse("local x = \"hello\" .. \"world\"");
        var pass = new StringEncryptPass();
        var opts = AllDisabled();
        opts.EncryptStrings = true;
        pass.Transform(root, opts);

        var code = Write(root);
        Assert.DoesNotContain("\"hello\"", code);
        Assert.DoesNotContain("\"world\"", code);
        Assert.Contains("__se_", code);
    }

    [Fact]
    public void StringEncryptPass_ReplacesStringInFunctionCall()
    {
        var root = Parse("print(\"hello world\")");
        var pass = new StringEncryptPass();
        var opts = AllDisabled();
        opts.EncryptStrings = true;
        pass.Transform(root, opts);

        var code = Write(root);
        Assert.DoesNotContain("\"hello world\"", code);
        Assert.Contains("__se_", code);
    }

    [Fact]
    public void StringEncryptPass_Disabled_DoesNothing()
    {
        var root = Parse("local x = \"hello\"");
        var pass = new StringEncryptPass();
        var opts = AllDisabled();
        pass.Transform(root, opts);

        var code = Write(root);
        Assert.Contains("\"hello\"", code);
    }

    [Fact]
    public void StringEncryptPass_EmptyScript_NoCrash()
    {
        var root = Parse("");
        var pass = new StringEncryptPass();
        var opts = AllDisabled();
        opts.EncryptStrings = true;
        var ex = Record.Exception(() => pass.Transform(root, opts));
        Assert.Null(ex);
    }

    [Fact]
    public void StringEncryptPass_NoStrings_NoInjection()
    {
        var root = Parse("local x = 42");
        var before = root.Statements.Count;
        var pass = new StringEncryptPass();
        var opts = AllDisabled();
        opts.EncryptStrings = true;
        pass.Transform(root, opts);

        Assert.Equal(before, root.Statements.Count);
    }

    // ── NumberEncodePass ────────────────────────────────────────────

    [Fact]
    public void NumberEncodePass_EncodesNumbers()
    {
        var root = Parse("local x = 42");
        var pass = new NumberEncodePass();
        var opts = AllDisabled();
        opts.EncodeNumbers = true;
        pass.Transform(root, opts);

        var code = Write(root);
        Assert.DoesNotContain("local x = 42", code);
        Assert.Contains("local x =", code);
    }

    [Fact]
    public void NumberEncodePass_EncodesAllNumberKinds()
    {
        var root = Parse("local a = 1; local b = 999999; local c = 0.5");
        var pass = new NumberEncodePass();
        var opts = AllDisabled();
        opts.EncodeNumbers = true;
        pass.Transform(root, opts);

        var code = Write(root);
        Assert.DoesNotContain("local a = 1", code);
        Assert.DoesNotContain("local b = 999999", code);
        Assert.DoesNotContain("local c = 0.5", code);
    }

    [Fact]
    public void NumberEncodePass_DoesNotEncodeZero()
    {
        var root = Parse("local x = 0");
        var pass = new NumberEncodePass();
        var opts = AllDisabled();
        opts.EncodeNumbers = true;
        pass.Transform(root, opts);

        var code = Write(root);
        Assert.Contains("local x = 0", code); // zero is not encoded
    }

    [Fact]
    public void NumberEncodePass_DoesNotEncodeNegativeSmall()
    {
        // Values < 0.001 should not be encoded
        var root = Parse("local x = 0.0005");
        var pass = new NumberEncodePass();
        var opts = AllDisabled();
        opts.EncodeNumbers = true;
        pass.Transform(root, opts);

        var code = Write(root);
        Assert.Contains("local x = 0.0005", code);
    }

    [Fact]
    public void NumberEncodePass_Disabled_DoesNothing()
    {
        var root = Parse("local x = 42");
        var pass = new NumberEncodePass();
        var opts = AllDisabled();
        pass.Transform(root, opts);

        var code = Write(root);
        Assert.Contains("local x = 42", code);
    }

    [Fact]
    public void NumberEncodePass_EncodesInBinaryExpr()
    {
        var root = Parse("local x = 10 + 20");
        var pass = new NumberEncodePass();
        var opts = AllDisabled();
        opts.EncodeNumbers = true;
        pass.Transform(root, opts);

        var code = Write(root);
        Assert.DoesNotContain("(10 + 20)", code);
    }

    // ── DeadCodePass ────────────────────────────────────────────────

    [Fact]
    public void DeadCodePass_InjectsStatements()
    {
        var root = Parse("local x = 1");
        var pass = new DeadCodePass();
        var opts = AllDisabled();
        opts.InjectDeadCode = true;
        opts.DeadCodeBlocks = 3;
        pass.Transform(root, opts);

        Assert.True(root.Statements.Count > 1);
    }

    [Fact]
    public void DeadCodePass_EmptyScript_NoCrash()
    {
        var root = Parse("");
        var pass = new DeadCodePass();
        var opts = AllDisabled();
        opts.InjectDeadCode = true;
        var ex = Record.Exception(() => pass.Transform(root, opts));
        Assert.Null(ex);
    }

    [Fact]
    public void DeadCodePass_Disabled_DoesNothing()
    {
        var root = Parse("local x = 1");
        var pass = new DeadCodePass();
        var opts = AllDisabled();
        pass.Transform(root, opts);

        Assert.Single(root.Statements);
    }

    [Fact]
    public void DeadCodePass_InjectsInsideFunctions()
    {
        var root = Parse("function foo() local a = 1 end");
        var funcDecl = (FunctionDeclStmt)root.Statements[0];
        var beforeCount = funcDecl.FuncExpr.Body.Statements.Count;

        var pass = new DeadCodePass();
        var opts = AllDisabled();
        opts.InjectDeadCode = true;
        opts.DeadCodeBlocks = 2;
        pass.Transform(root, opts);

        Assert.True(funcDecl.FuncExpr.Body.Statements.Count > beforeCount);
    }

    // ── ControlFlowPass ─────────────────────────────────────────────

    [Fact]
    public void ControlFlowPass_WrapsStatements()
    {
        // Use statements that ControlFlowPass will wrap: AssignmentStmt, FunctionCallStmt, etc.
        var root = Parse("x = 1; print(2); y = 3");
        var pass = new ControlFlowPass();
        var opts = AllDisabled();
        opts.ObfuscateControlFlow = true;
        pass.Transform(root, opts);

        var code = Write(root);
        Assert.True(code.Contains("do") || code.Contains("if"));
    }

    [Fact]
    public void ControlFlowPass_EmptyScript_NoCrash()
    {
        var root = Parse("");
        var pass = new ControlFlowPass();
        var opts = AllDisabled();
        opts.ObfuscateControlFlow = true;
        var ex = Record.Exception(() => pass.Transform(root, opts));
        Assert.Null(ex);
    }

    [Fact]
    public void ControlFlowPass_Disabled_DoesNothing()
    {
        var root = Parse("local x = 1");
        var pass = new ControlFlowPass();
        var opts = AllDisabled();
        pass.Transform(root, opts);

        var code = Write(root);
        Assert.Contains("local x = 1", code);
        Assert.DoesNotContain("do", code);
        Assert.DoesNotContain("if", code);
    }

    [Fact]
    public void ControlFlowPass_SingleStmt_Skipped()
    {
        var root = Parse("local x = 1");
        var before = Write(root);
        var pass = new ControlFlowPass();
        var opts = AllDisabled();
        opts.ObfuscateControlFlow = true;
        pass.Transform(root, opts);

        var after = Write(root);
        Assert.Equal(before, after);
    }

    // ── ExpressionSplitPass ─────────────────────────────────────────

    [Fact]
    public void ExpressionSplitPass_MaySplitExpressions()
    {
        var root = Parse("local x = 1 + 2 + 3 + 4 + 5 + 6");
        var pass = new ExpressionSplitPass();
        var opts = AllDisabled();
        opts.SplitExpressions = true;
        pass.Transform(root, opts);

        var code = Write(root);
        // May introduce a tmp local (probabilistic), but should always be valid
        Assert.Contains("local x", code);
    }

    [Fact]
    public void ExpressionSplitPass_ValidAfterSplit()
    {
        var root = Parse("local x = 1 + 2");
        var pass = new ExpressionSplitPass();
        var opts = AllDisabled();
        opts.SplitExpressions = true;
        var ex = Record.Exception(() => pass.Transform(root, opts));
        Assert.Null(ex);
    }

    [Fact]
    public void ExpressionSplitPass_EmptyScript_NoCrash()
    {
        var root = Parse("");
        var pass = new ExpressionSplitPass();
        var opts = AllDisabled();
        opts.SplitExpressions = true;
        var ex = Record.Exception(() => pass.Transform(root, opts));
        Assert.Null(ex);
    }

    // ── AntiDebugPass ───────────────────────────────────────────────

    [Fact]
    public void AntiDebugPass_InjectsChecks()
    {
        var root = Parse("local x = 1");
        var originalCount = root.Statements.Count;
        var pass = new AntiDebugPass();
        var opts = AllDisabled();
        opts.AntiDebug = true;
        pass.Transform(root, opts);

        Assert.True(root.Statements.Count > originalCount);
    }

    [Fact]
    public void AntiDebugPass_Disabled_DoesNothing()
    {
        var root = Parse("local x = 1");
        var originalCount = root.Statements.Count;
        var pass = new AntiDebugPass();
        var opts = AllDisabled();
        pass.Transform(root, opts);

        Assert.Equal(originalCount, root.Statements.Count);
    }

    [Fact]
    public void AntiDebugPass_EmptyScript_NoCrash()
    {
        var root = Parse("");
        var pass = new AntiDebugPass();
        var opts = AllDisabled();
        opts.AntiDebug = true;
        var ex = Record.Exception(() => pass.Transform(root, opts));
        Assert.Null(ex);
    }

    // ── Full Pipeline ───────────────────────────────────────────────

    [Fact]
    public void FullPipeline_NoCrash()
    {
        var src = "local x = 42\nprint(\"hello\")\nlocal t = {a = 1, b = 2}";
        var processor = new LunarGuardProcessor();
        var result = processor.Process(src);
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void FullPipeline_AllDisabled_NoChange()
    {
        var src = "local x = 42\nprint(\"hello\")";
        var options = new ObfuscationOptions
        {
            RenameVariables = false,
            EncryptStrings = false,
            EncodeNumbers = false,
            InjectDeadCode = false,
            ObfuscateControlFlow = false,
            SplitExpressions = false,
            AntiDebug = false,
            Virtualize = false
        };
        var processor = new LunarGuardProcessor();
        var result = processor.Process(src, options);
        // With all passes disabled, output should be close to original
        // (minor whitespace differences from writer)
        Assert.Contains("local x = 42", result);
        Assert.Contains("hello", result);
    }

    [Fact]
    public void FullPipeline_WithRenameOnly()
    {
        var src = "local x = 1; local y = x + 1; print(y)";
        var options = new ObfuscationOptions
        {
            RenameVariables = true,
            EncryptStrings = false,
            EncodeNumbers = false,
            InjectDeadCode = false,
            ObfuscateControlFlow = false,
            SplitExpressions = false,
            AntiDebug = false,
            Virtualize = false
        };
        var processor = new LunarGuardProcessor();
        var result = processor.Process(src, options);
        Assert.DoesNotContain("local x", result);
        Assert.Contains("print(", result);
    }

    [Fact]
    public void FullPipeline_EmptyScript_NoCrash()
    {
        var processor = new LunarGuardProcessor();
        var result = processor.Process("");
        Assert.NotNull(result);
    }

    [Fact]
    public void FullPipeline_ComplexScript_NoCrash()
    {
        var src = """
            local function fib(n)
                if n <= 1 then return n end
                return fib(n - 1) + fib(n - 2)
            end

            for i = 1, 10 do
                print(fib(i))
            end
            """;
        var options = new ObfuscationOptions
        {
            RenameVariables = true,
            EncryptStrings = true,
            EncodeNumbers = true,
            InjectDeadCode = true,
            ObfuscateControlFlow = true,
            SplitExpressions = true,
            AntiDebug = true,
            Virtualize = false // VirtualizationPass doesn't handle DoStmt yet
        };
        var processor = new LunarGuardProcessor();
        var result = processor.Process(src, options);
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.True(result.Length > 10);
    }

    [Fact]
    public void FullPipeline_OutputDiffersFromInput()
    {
        var src = "local message = \"hello world\"\nprint(message)";
        var processor = new LunarGuardProcessor();
        var result = processor.Process(src);
        Assert.NotEqual(src, result);
    }

    // ── VirtualizationPass ──────────────────────────────────────────

    [Fact]
    public void VirtualizationPass_NoCrash()
    {
        var root = Parse("local x = 1; local y = x + 1");
        var pass = new VirtualizationPass();
        var opts = AllDisabled();
        opts.Virtualize = true;
        var ex = Record.Exception((Action)(() => pass.Transform(root, opts)));
        Assert.Null(ex);
    }

    [Fact]
    public void VirtualizationPass_OutputContainsVM()
    {
        var root = Parse("local x = 1");
        var pass = new VirtualizationPass();
        var opts = AllDisabled();
        opts.Virtualize = true;
        pass.Transform(root, opts);

        var code = Write(root);
        Assert.Contains("function", code);
        Assert.True(code.Length > 100);
    }

    // ── Parse + Write Roundtrip ──────────────────────────────────

    [Theory]
    [InlineData("local x = 42")]
    [InlineData("for i = 1, 10 do print(i) end")]
    [InlineData("local t = {a = 1, [2] = \"two\"}")]
    [InlineData("function foo() return nil end")]
    [InlineData("do local x = 1 end")]
    [InlineData("repeat local x = 1 until x > 10")]
    [InlineData("while true do break end")]
    [InlineData("local function f() end")]
    public void ParseWrite_Roundtrip_ValidLua(string src)
    {
        var root = Parse(src);
        var code = Write(root);
        Assert.NotEmpty(code);
        // Re-parse the output to verify it's valid Lua
        var reLexer = new Lexer(code);
        var reTokens = reLexer.Tokenize();
        var reParser = new Parser(reTokens);
        var ex = Record.Exception(() => reParser.Parse());
        Assert.Null(ex);
    }

    // ── MultiPass Edge Cases ────────────────────────────────────

    [Fact]
    public void AllPasses_RunSequentially_NoCrash()
    {
        var root = Parse("""
            local name = "world"
            local function greet(n)
                if n == nil then return "hello" end
                return "hello " .. n
            end
            print(greet(name))
            """);

        var opts = new ObfuscationOptions
        {
            RenameVariables = true,
            EncryptStrings = true,
            EncodeNumbers = true,
            InjectDeadCode = true,
            DeadCodeBlocks = 3,
            ObfuscateControlFlow = true,
            SplitExpressions = true,
            AntiDebug = true,
            Virtualize = false // skip virtualization for this combined test
        };

        var passes = new IObfuscationPass[]
        {
            new RenamePass(),
            new DeadCodePass(),
            new StringEncryptPass(),
            new ControlFlowPass(),
            new ExpressionSplitPass(),
            new AntiDebugPass(),
            new NumberEncodePass()
        };

        foreach (var pass in passes)
        {
            var ex = Record.Exception(() => pass.Transform(root, opts));
            Assert.Null(ex);
        }
    }

    [Fact]
    public void PassManager_RunAll_NoCrash()
    {
        var root = Parse("local x = 1; local y = x + 1; print(y)");
        var pm = new PassManager();
        pm.Register(new RenamePass());
        pm.Register(new DeadCodePass());
        pm.Register(new StringEncryptPass());
        pm.Register(new ControlFlowPass());
        pm.Register(new ExpressionSplitPass());
        pm.Register(new AntiDebugPass());
        pm.Register(new NumberEncodePass());

        var opts = new ObfuscationOptions
        {
            RenameVariables = true,
            EncryptStrings = true,
            EncodeNumbers = true,
            InjectDeadCode = true,
            DeadCodeBlocks = 3,
            ObfuscateControlFlow = true,
            SplitExpressions = true,
            AntiDebug = true,
            Virtualize = false
        };

        var ex = Record.Exception(() => pm.RunAll(root, opts));
        Assert.Null(ex);

        var writer = new LuaWriter();
        var code = writer.Write(root);
        Assert.NotEmpty(code);
    }
}
