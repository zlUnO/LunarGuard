using LunarGuard.Core.AST;
using LunarGuard.Core.AST.Stmt;
using LunarGuard.Core.AST.Expr;

namespace LunarGuard.Core.Obfuscation;

public class ControlFlowPass : IObfuscationPass
{
    public string Name => "Control Flow Obfuscation";

    public void Transform(BlockStmt root, ObfuscationOptions options)
    {
        if (!options.ObfuscateControlFlow) return;

        var rng = new Random();
        ProcessBlock(root, rng);
    }

    private static void ProcessBlock(BlockStmt block, Random rng)
    {
        if (block.Statements.Count < 2) return;

        var stmts = block.Statements.ToList();
        var insertPoints = new List<int>();

        for (var i = 0; i < stmts.Count; i++)
            if (stmts[i] is not ReturnStmt and not FunctionDeclStmt
                and not BreakStmt and not GotoStmt and not LabelStmt)
                insertPoints.Add(i);

        if (insertPoints.Count < 2) return;

        for (var i = 0; i < insertPoints.Count; i++)
        {
            var idx = insertPoints[i];
            var stmt = stmts[idx];
            if (stmt is not LocalVarStmt and (AssignmentStmt or FunctionCallStmt or IfStmt or WhileStmt or DoStmt))
            {
                stmts[idx] = rng.Next(3) switch
                {
                    0 => WrapInDo(stmt),
                    1 => WrapInOpaquePredicate(stmt, rng),
                    _ => WrapInDoubleDo(stmt),
                };
            }
        }

        block.Statements.Clear();
        block.Statements.AddRange(stmts);
    }

    private static Statement WrapInDo(Statement stmt)
    {
        return new DoStmt
        {
            Body = new BlockStmt { Statements = { stmt } }
        };
    }

    private static Statement WrapInDoubleDo(Statement stmt)
    {
        return new DoStmt
        {
            Body = new BlockStmt
            {
                Statements =
                {
                    new DoStmt
                    {
                        Body = new BlockStmt { Statements = { stmt } }
                    }
                }
            }
        };
    }

    private static Statement WrapInOpaquePredicate(Statement stmt, Random rng)
    {
        var a = rng.Next(100000, 999999);
        var b = a + rng.Next(1, 100);
        return new IfStmt
        {
            Branches =
            {
                (
                    new BinaryExpr(BinaryOp.Lt,
                        new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)a),
                        new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)b)),
                    new BlockStmt { Statements = { stmt } }
                )
            },
            ElseBody = new BlockStmt
            {
                Statements =
                {
                    new LocalVarStmt
                    {
                        Names = { $"_{rng.Next():x8}" },
                        Values = { new LiteralExpr(LiteralExpr.LiteralKind.Nil, null) }
                    }
                }
            }
        };
    }
}