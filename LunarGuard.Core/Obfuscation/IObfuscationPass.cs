using LunarGuard.Core.AST.Stmt;

namespace LunarGuard.Core.Obfuscation;

public interface IObfuscationPass
{
    string Name { get; }
    void Transform(BlockStmt root, ObfuscationOptions options);
}
