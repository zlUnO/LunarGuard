using System.Security.Cryptography;
using LunarGuard.Core.AST;
using LunarGuard.Core.AST.Stmt;
using LunarGuard.Core.AST.Expr;

namespace LunarGuard.Core.Obfuscation;

public class OpaquePredicatePass : IObfuscationPass
{
    public string Name => "Opaque Predicates";

    private static readonly string[] AlwaysTrueStrings = {
        "os.clock()", "type(nil)", "0 == 0", "1 == 1",
        "true", "not false", "(1 + 1) == 2"
    };

    public void Transform(BlockStmt root, ObfuscationOptions options)
    {
        if (!options.OpaquePredicates) return;
        ProcessBlock(root);
    }

    private static void ProcessBlock(BlockStmt block)
    {
        var newStmts = new List<Statement>();
        foreach (var stmt in block.Statements.ToList())
        {
            switch (stmt)
            {
                case IfStmt ifStmt:
                    foreach (var (_, body) in ifStmt.Branches)
                        ProcessBlock(body);
                    if (ifStmt.ElseBody != null)
                        ProcessBlock(ifStmt.ElseBody);
                    break;
                case WhileStmt w:
                    ProcessBlock(w.Body);
                    break;
                case RepeatStmt r:
                    ProcessBlock(r.Body);
                    break;
                case ForNumericStmt fn:
                    ProcessBlock(fn.Body);
                    break;
                case ForGenericStmt fg:
                    ProcessBlock(fg.Body);
                    break;
                case DoStmt d:
                    ProcessBlock(d.Body);
                    break;
                case FunctionDeclStmt fd:
                    ProcessBlock(fd.FuncExpr.Body);
                    break;
            }

            // Never wrap declaration statements (LocalVarStmt): doing so would
            // restrict the new binding's scope to the wrapping `if` block and
            // break subsequent siblings that reference the declared name
            // (e.g. `local player = {}` followed by `function player:takeDamage`).
            if (stmt is not (IfStmt or WhileStmt or RepeatStmt or ForNumericStmt or ForGenericStmt or DoStmt or FunctionDeclStmt or LocalVarStmt or ReturnStmt or BreakStmt or GotoStmt))
            {
                var predicate = CreateOpaquePredicate();
                var wrapper = new IfStmt();
                wrapper.Branches.Add((predicate, new BlockStmt()));
                wrapper.Branches[0].Body.Statements.Add(stmt);
                var idx = block.Statements.IndexOf(stmt);
                block.Statements[idx] = wrapper;
                block.Statements.InsertRange(idx + 1, newStmts);
                newStmts.Clear();
            }
        }
    }

    private static Expression CreateOpaquePredicate()
    {
        using var rng = RandomNumberGenerator.Create();
        var buf = new byte[1];
        rng.GetBytes(buf);
        var variant = buf[0] % 6;

        return variant switch
        {
            0 => new BinaryExpr(BinaryOp.Add,
                new LiteralExpr(LiteralExpr.LiteralKind.Number, 1L),
                new LiteralExpr(LiteralExpr.LiteralKind.Number, 1L)),
            1 => new BinaryExpr(BinaryOp.Eq,
                new LiteralExpr(LiteralExpr.LiteralKind.Number, 42L),
                new LiteralExpr(LiteralExpr.LiteralKind.Number, 42L)),
            2 => new UnaryExpr(UnaryOp.Not,
                new LiteralExpr(LiteralExpr.LiteralKind.Boolean, false)),
            3 => new BinaryExpr(BinaryOp.Geq,
                new LiteralExpr(LiteralExpr.LiteralKind.Number, long.MaxValue),
                new LiteralExpr(LiteralExpr.LiteralKind.Number, 1L)),
            4 => new BinaryExpr(BinaryOp.Multiply,
                new UnaryExpr(UnaryOp.Length,
                    new LiteralExpr(LiteralExpr.LiteralKind.String, "x")),
                new LiteralExpr(LiteralExpr.LiteralKind.Number, 0L)),
            _ => new LiteralExpr(LiteralExpr.LiteralKind.Boolean, true),
        };
    }
}
