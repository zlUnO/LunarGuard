using LunarGuard.Core.AST;
using LunarGuard.Core.AST.Stmt;

namespace LunarGuard.Core.Obfuscation;

public class PassManager
{
    private readonly List<IObfuscationPass> _passes = new();

    public void Register(IObfuscationPass pass)
    {
        _passes.Add(pass);
    }

    public void RunAll(BlockStmt root, ObfuscationOptions options)
    {
        foreach (var pass in _passes)
            pass.Transform(root, options);
    }
}
