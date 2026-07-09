using System.Text;
using System.Text.RegularExpressions;
using LunarGuard.Core.AST;
using LunarGuard.Core.AST.Stmt;
using LunarGuard.Core.CodeGen;
using LunarGuard.Core.Obfuscation;
using LunarGuard.Core.Obfuscation.Virtualization;
using LunarGuard.Core.Syntax;

namespace LunarGuard.Core;

public class LunarGuardProcessor
{
    private static int _chunkId;

    public string Process(string source, ObfuscationOptions? options = null)
    {
        options ??= new ObfuscationOptions();

        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();

        var parser = new Parser(tokens);
        var stmts = parser.Parse();
        var root = new BlockStmt();
        root.Statements.AddRange(stmts);

        var pm = new PassManager();
        pm.Register(new AstOptimizationPass());
        pm.Register(new RenamePass());
        pm.Register(new StringSplitPass());
        pm.Register(new OpaquePredicatePass());
        pm.Register(new DeadCodePass());
        pm.Register(new StringEncryptPass());
        pm.Register(new ControlFlowPass());
        pm.Register(new ExpressionSplitPass());
        pm.Register(new AntiDebugPass());
        pm.Register(new NumberEncodePass());
        pm.Register(new VirtualizationPass());
        pm.Register(new AntiTamperPass());

        pm.RunAll(root, options);

        var writer = new LuaWriter();
        var code = writer.Write(root);

        // Wrap in loadstring to avoid Lua 5.1's 200-local-variable limit on root chunk
        return WrapInLoadstring(code);
    }

    /// <summary>
    /// Wrap generated code in loadstring(...)() to bypass Lua 5.1's 200-local limit.
    /// Uses long bracket syntax [=[ ... ]=] with auto-detected separator count.
    /// </summary>
    private static string WrapInLoadstring(string code)
    {
        // Find the required number of = signs to avoid collision
        var eqCount = 0;
        while (true)
        {
            var testClose = eqCount == 0 ? "]]" : $"]{new string('=', eqCount)}]";
            if (!code.Contains(testClose))
                break;
            eqCount++;
        }

        var chunkName = $"[lunarguard_{++_chunkId}]";
        var bracketOpen = eqCount == 0 ? "[[" : $"[{new string('=', eqCount)}[";
        var bracketClose = eqCount == 0 ? "]]" : $"]{new string('=', eqCount)}]";

        return $"loadstring({bracketOpen}\n{code}\n{bracketClose}, \"{chunkName}\")()";
    }

    public string ProcessFile(string inputPath, string? outputPath = null, ObfuscationOptions? options = null)
    {
        var fileInfo = new FileInfo(inputPath);
        const long maxSize = 10L * 1024 * 1024;
        if (fileInfo.Length > maxSize)
            throw new InvalidOperationException($"Input file too large ({fileInfo.Length:N0} bytes). Maximum allowed: 10 MiB.");

        var source = File.ReadAllText(inputPath);
        var result = Process(source, options);
        outputPath ??= Path.ChangeExtension(inputPath, ".obfuscated.lua");
        var outputFull = Path.GetFullPath(outputPath);
        var cwd = Path.GetFullPath(Environment.CurrentDirectory);
        if (!outputFull.StartsWith(cwd + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(outputFull, cwd, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Output path must be within the current working directory.");
        File.WriteAllText(outputFull, result);
        return outputFull;
    }
}
