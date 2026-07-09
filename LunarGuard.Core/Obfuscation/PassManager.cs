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

    public void RunOrdered(BlockStmt root, ObfuscationOptions options, List<string>? order = null)
    {
        if (order == null || order.Count == 0)
        {
            RunAll(root, options);
            return;
        }

        var passMap = _passes.ToDictionary(p => p.Name);
        var runNames = new HashSet<string>();

        foreach (var passName in order)
        {
            if (passMap.TryGetValue(passName, out var pass))
            {
                pass.Transform(root, options);
                runNames.Add(passName);
            }
        }

        foreach (var pass in _passes)
        {
            if (!runNames.Contains(pass.Name))
                pass.Transform(root, options);
        }
    }
}
