using LunarGuard.Core.AST;
using LunarGuard.Core.AST.Stmt;
using LunarGuard.Core.CodeGen;
using LunarGuard.Core.Obfuscation;
using LunarGuard.Core.Obfuscation.Virtualization;
using LunarGuard.Core.Syntax;

namespace LunarGuard.Core;

public class LunarGuardProcessor
{
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
        pm.Register(new RenamePass());
        pm.Register(new DeadCodePass());
        pm.Register(new StringEncryptPass());
        pm.Register(new ControlFlowPass());
        pm.Register(new ExpressionSplitPass());
        pm.Register(new AntiDebugPass());
        pm.Register(new NumberEncodePass());
        pm.Register(new VirtualizationPass());

        pm.RunAll(root, options);

        var writer = new LuaWriter();
        return writer.Write(root);
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
        if (!outputFull.StartsWith(cwd, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Output path must be within the current working directory.");
        File.WriteAllText(outputFull, result);
        return outputFull;
    }
}
