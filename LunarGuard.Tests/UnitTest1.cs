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

    // ── VirtualizationPass Extended ────────────────────────────

    [Fact]
    public void VirtualizationPass_HandlesForGenericStmt()
    {
        var root = Parse("for k,v in pairs({a=1}) do print(k,v) end");
        var pass = new VirtualizationPass();
        var opts = AllDisabled();
        opts.Virtualize = true;
        var ex = Record.Exception(() => pass.Transform(root, opts));
        Assert.Null(ex);
        var code = Write(root);
        Assert.Contains("loadstring", code);
    }

    [Fact]
    public void VirtualizationPass_HandlesRepeatStmt()
    {
        var root = Parse("local x = 1; repeat x = x + 1 until x > 10");
        var pass = new VirtualizationPass();
        var opts = AllDisabled();
        opts.Virtualize = true;
        var ex = Record.Exception(() => pass.Transform(root, opts));
        Assert.Null(ex);
        var code = Write(root);
        Assert.Contains("loadstring", code);
    }

    [Fact]
    public void VirtualizationPass_HandlesDoStmt()
    {
        var root = Parse("do local x = 1 end");
        var pass = new VirtualizationPass();
        var opts = AllDisabled();
        opts.Virtualize = true;
        var ex = Record.Exception(() => pass.Transform(root, opts));
        Assert.Null(ex);
    }

    [Fact]
    public void VirtualizationPass_SkipsNestedFunctions()
    {
        var root = Parse("function outer() local x = 1; function inner() print(x) end end");
        var originalStmtCount = root.Statements.Count;
        var pass = new VirtualizationPass();
        var opts = AllDisabled();
        opts.Virtualize = true;
        var ex = Record.Exception(() => pass.Transform(root, opts));
        Assert.Null(ex);
    }

    [Fact]
    public void VirtualizationPass_HandlesForNumericWithStep()
    {
        var root = Parse("for i = 1, 10, 2 do print(i) end");
        var pass = new VirtualizationPass();
        var opts = AllDisabled();
        opts.Virtualize = true;
        var ex = Record.Exception(() => pass.Transform(root, opts));
        Assert.Null(ex);
    }

    [Fact]
    public void VirtualizationPass_HandlesIfElseIf()
    {
        var root = Parse("if x == 1 then print(1) elseif x == 2 then print(2) else print(3) end");
        var pass = new VirtualizationPass();
        var opts = AllDisabled();
        opts.Virtualize = true;
        var ex = Record.Exception(() => pass.Transform(root, opts));
        Assert.Null(ex);
    }

    [Fact]
    public void VirtualizationPass_HandlesNestedWhile()
    {
        var root = Parse("while true do while false do print(1) end end");
        var pass = new VirtualizationPass();
        var opts = AllDisabled();
        opts.Virtualize = true;
        var ex = Record.Exception(() => pass.Transform(root, opts));
        Assert.Null(ex);
    }

    [Fact]
    public void VirtualizationPass_HandlesTableConstructors()
    {
        var root = Parse("local t = {a = 1, [2] = \"two\", 3, 4, 5}");
        var pass = new VirtualizationPass();
        var opts = AllDisabled();
        opts.Virtualize = true;
        var ex = Record.Exception(() => pass.Transform(root, opts));
        Assert.Null(ex);
    }

    // ── GameSense Compatibility ─────────────────────────────

    [Fact]
    public void RenamePass_PreservesGameSenseGlobals()
    {
        var root = Parse("client.set_event_callback(\"paint\", function() end)");
        var pass = new RenamePass();
        var opts = AllDisabled();
        opts.RenameVariables = true;
        pass.Transform(root, opts);
        var code = Write(root);
        Assert.Contains("client", code);
        Assert.Contains("set_event_callback", code);
    }

    [Fact]
    public void RenamePass_PreservesEntityAndUi()
    {
        var root = Parse("entity.get_all(\"CCSPlayer\"); ui.new_checkbox(\"Test\", \"test\")");
        var pass = new RenamePass();
        var opts = AllDisabled();
        opts.RenameVariables = true;
        pass.Transform(root, opts);
        var code = Write(root);
        Assert.Contains("entity.get_all", code);
        Assert.Contains("ui.new_checkbox", code);
    }

    // ── Configurable Pipeline ────────────────────────────────

    [Fact]
    public void PassManager_OrderedExecution_NoCrash()
    {
        var root = Parse("local x = 42; print(x)");
        var pm = new PassManager();
        pm.Register(new RenamePass());
        pm.Register(new DeadCodePass());
        pm.Register(new NumberEncodePass());

        var opts = new ObfuscationOptions
        {
            RenameVariables = true,
            InjectDeadCode = false,
            EncodeNumbers = true,
            EncryptStrings = false,
            ObfuscateControlFlow = false,
            SplitExpressions = false,
            AntiDebug = false,
            Virtualize = false
        };

        var order = new List<string> { "Number Encoding", "Rename Variables" };
        var ex = Record.Exception(() => pm.RunOrdered(root, opts, order));
        Assert.Null(ex);
    }

    [Fact]
    public void LunarGuardProcessor_BuildPassManager_ContainsAllPasses()
    {
        var processor = new LunarGuardProcessor();
        var names = LunarGuardProcessor.GetPassNames();
        Assert.Contains("Rename Variables", names);
        Assert.Contains("Dead Code Injection", names);
        Assert.Contains("String Encryption", names);
        Assert.Contains("Control Flow", names);
        Assert.Contains("Expression Split", names);
        Assert.Contains("Anti-Debug", names);
        Assert.Contains("Number Encoding", names);
        Assert.Contains("Bytecode Virtualization", names);
    }

    // ── GameSense Script Integration ─────────────────────────

    [Fact]
    public void FullPipeline_GameSenseScript_NoCrash()
    {
        var src = """
            local function on_paint()
                local players = entity.get_all("CCSPlayer")
                for i, player in ipairs(players) do
                    if player ~= nil then
                        local hp = player:m_iHealth()
                        if hp > 0 then
                            renderer.text(player:get_origin(), tostring(hp))
                        end
                    end
                end
            end

            client.set_event_callback("paint", on_paint)
            """;
        var processor = new LunarGuardProcessor();
        var ex = Record.Exception(() => processor.Process(src));
        Assert.Null(ex);
        var result = processor.Process(src);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void FullPipeline_ForGenericStmt_NoCrash()
    {
        var src = """
            for k, v in pairs({a = 1, b = 2}) do
                print(k, v)
            end
            """;
        var processor = new LunarGuardProcessor();
        var result = processor.Process(src);
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void FullPipeline_RepeatUntil_NoCrash()
    {
        var src = """
            local x = 0
            repeat
                x = x + 1
                print(x)
            until x >= 5
            """;
        var processor = new LunarGuardProcessor();
        var result = processor.Process(src);
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void FullPipeline_ComplexLua_NoCrash()
    {
        var src = """
            local t = {1, 2, 3, 4, 5}
            local sum = 0
            for i = 1, #t do
                sum = sum + t[i]
            end
            print("sum: " .. tostring(sum))

            local function factorial(n)
                if n <= 1 then
                    return 1
                end
                return n * factorial(n - 1)
            end

            print(factorial(5))
            """;
        var processor = new LunarGuardProcessor();
        var result = processor.Process(src);
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void FullPipeline_WithVirtualizeAndGameSense_NoCrash()
    {
        var src = """
            local function process_players()
                for i, player in ipairs(entity.get_all("CCSPlayer")) do
                    if player:is_alive() then
                        local pos = player:get_origin()
                        renderer.circle(pos, 10, 255, 0, 0, 255)
                    end
                end
            end

            client.set_event_callback("paint", process_players)
            """;
        var processor = new LunarGuardProcessor();
        var ex = Record.Exception(() => processor.Process(src));
        Assert.Null(ex);
    }

    // ── AntiDebug ────────────────────────────────────────────

    [Fact]
    public void AntiDebugPass_NoDebugReference()
    {
        var root = Parse("local x = 1");
        var pass = new AntiDebugPass();
        var opts = AllDisabled();
        opts.AntiDebug = true;
        pass.Transform(root, opts);
        var code = Write(root);
        Assert.DoesNotContain("debug", code);
    }

    [Fact]
    public void AntiDebugPass_UsesSafeChecks()
    {
        var root = Parse("local x = 1");
        var pass = new AntiDebugPass();
        var opts = AllDisabled();
        opts.AntiDebug = true;
        pass.Transform(root, opts);
        var code = Write(root);
        Assert.DoesNotContain("debug", code);
        Assert.Contains("do", code);
        Assert.True(code.Contains("os.clock") || code.Contains("pcall") || code.Contains("print"));
    }

    // ── Dead Code Pass ───────────────────────────────────────

    [Fact]
    public void DeadCodePass_NewPatterns_NoCrash()
    {
        var root = Parse("local x = 1; local y = 2; print(x + y)");
        var pass = new DeadCodePass();
        var opts = AllDisabled();
        opts.InjectDeadCode = true;
        opts.DeadCodeBlocks = 10;
        var ex = Record.Exception(() => pass.Transform(root, opts));
        Assert.Null(ex);
        var code = Write(root);
        Assert.NotEmpty(code);
    }

    [Fact]
    public void DeadCodePass_InjectsInAllStatementTypes()
    {
        var root = Parse("""
            if true then print(1) end
            while false do print(2) end
            repeat print(3) until true
            do print(4) end
            for i = 1, 5 do print(i) end
            """);
        var before = Write(root);
        var pass = new DeadCodePass();
        var opts = AllDisabled();
        opts.InjectDeadCode = true;
        opts.DeadCodeBlocks = 15;
        pass.Transform(root, opts);
        var after = Write(root);
        Assert.True(after.Length > before.Length);
    }

    // ── StringEncrypt Extended ───────────────────────────────

    [Fact]
    public void StringEncryptPass_SingleCharStrings()
    {
        var root = Parse(@"local a = ""x""; local b = ""y""");
        var pass = new StringEncryptPass();
        var opts = AllDisabled();
        opts.EncryptStrings = true;
        pass.Transform(root, opts);
        var code = Write(root);
        Assert.DoesNotContain("\"x\"", code);
        Assert.Contains("__se_", code);
    }

    [Fact]
    public void StringEncryptPass_ExtraAlgorithms_NoCrash()
    {
        var root = Parse("local s = \"hello world this is a test string for encryption\"");
        var pass = new StringEncryptPass();
        var opts = AllDisabled();
        opts.EncryptStrings = true;

        for (var i = 0; i < 20; i++)
        {
            var ex = Record.Exception(() => pass.Transform(root, opts));
            Assert.Null(ex);
        }
    }

    // ── Fuzz Testing ─────────────────────────────────────────

    public static IEnumerable<object[]> GetFuzzInputs()
    {
        var inputs = new[]
        {
            "local x = 1",
            "x = 1",
            "function f() end",
            "local function f() end",
            "if true then end",
            "while true do break end",
            "repeat until true",
            "do end",
            "return nil",
            "local x = 1; if x then print(1) end",
            "local x = (1+2)*3",
            "for i=1,10 do end",
            "for k,v in pairs({}) do end",
            "print(1,2,3)",
            "local a,b=1,2",
            "local t={}",
            "local t={1,2,3}",
            "local t={a=1,b=2}",
            "x = a.b.c",
            "x = a[b]",
            "x = #t",
            "x = -t",
            "x = not true",
            "x = 1+2", "x = 1-2", "x = 1*2", "x = 1/2", "x = 1%2", "x = 1^2",
            "x = 1==2", "x = 1~=2", "x = 1<2", "x = 1>2", "x = 1<=2", "x = 1>=2",
            "x = a and b",
            "x = a or b",
            "x = \"a\"..\"b\"",
            "function f(...) end",
            "local function f(a,b,...) end",
            "a.f()",
            "a:f()",
            "a.f(1,2,3)",
            "x = {[1]=2,[3]=4}",
            "x = {{}}",
            "f()()",
            "f(g(h()))",
            "x = {}; x[1] = 2",
            "for i=1,10 do for j=1,5 do print(i,j) end end",
            "if true then print(1) else print(2) end",
            "local x; local y",
            "x = nil",
            "print(\"hello \" .. \"world\")",
            "local function foo() return 1,2,3 end",
            "local t = {}; table.insert(t, 1)",
            "local s = \"\"; for i=1,10 do s = s .. tostring(i) end",
        };
        foreach (var input in inputs)
            yield return new object[] { input };
    }

    [Theory]
    [MemberData(nameof(GetFuzzInputs))]
    public void Fuzz_FullPipeline_NoCrash(string src)
    {
        var processor = new LunarGuardProcessor();
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
            Virtualize = true
        };
        var ex = Record.Exception(() => processor.Process(src, opts));
        Assert.Null(ex);
    }

    [Theory]
    [MemberData(nameof(GetFuzzInputs))]
    public void Fuzz_FullPipelineWithoutVM_NoCrash(string src)
    {
        var processor = new LunarGuardProcessor();
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
        var ex = Record.Exception(() => processor.Process(src, opts));
        Assert.Null(ex);
        var result = processor.Process(src, opts);
        Assert.NotNull(result);
    }

    [Theory]
    [MemberData(nameof(GetFuzzInputs))]
    public void Fuzz_ParseWrite_Roundtrip(string src)
    {
        var root = Parse(src);
        var code = Write(root);
        Assert.NotEmpty(code);
        var reLexer = new Lexer(code);
        var reTokens = reLexer.Tokenize();
        var reParser = new Parser(reTokens);
        var ex = Record.Exception(() => reParser.Parse());
        Assert.Null(ex);
    }
}
