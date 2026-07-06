using System.Security.Cryptography;
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

        using var rng = RandomNumberGenerator.Create();
        ProcessBlock(root, rng);
    }

    private static void ProcessBlock(BlockStmt block, RandomNumberGenerator rng)
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
            if (stmt is not LocalVarStmt && (stmt is AssignmentStmt or FunctionCallStmt or IfStmt or WhileStmt or DoStmt))
            {
                var buf = new byte[1];
                rng.GetBytes(buf);
                stmts[idx] = (buf[0] % 4) switch
                {
                    0 => WrapInDo(stmt),
                    1 => WrapInArithOpaque(stmt, rng),
                    2 => WrapInDoubleDo(stmt),
                    _ => WrapInNestedOpaque(stmt, rng),
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

    private static Statement WrapInArithOpaque(Statement stmt, RandomNumberGenerator rng)
    {
        var buf = new byte[8];
        rng.GetBytes(buf);
        var a = Math.Max(1, (BitConverter.ToInt32(buf, 0) & 0x7FFFFFFF) % 10000 + 1);
        var b = Math.Max(1, (BitConverter.ToInt32(buf, 4) & 0x7FFFFFFF) % 10000 + 1);

        // Opaque predicate: (a*a) % 2 == 0  (always true for any integer a)
        // More sophisticated: ((a+b)*(a+b)) % 2 == ((a*a) + 2*a*b + (b*b)) % 2 == (a*a + b*b) % 2
        // Since a%2 == a*a%2, and (a%2 + b%2) % 2 == (a+b)%2
        // This is always true mathematically
        
        var buf2 = new byte[4];
        rng.GetBytes(buf2);
        var useAlwaysTrue = (buf2[0] & 1) == 0;

        if (useAlwaysTrue)
        {
            // Always-true: (a*a) % 2 == 0 == (a%2 == 0)
            // False for odd a, true for even a. So we must ensure a is even.
            if (a % 2 != 0) a++;
            return new IfStmt
            {
                Branches =
                {
                    (
                        new BinaryExpr(BinaryOp.Eq,
                            new BinaryExpr(BinaryOp.Modulo,
                                new BinaryExpr(BinaryOp.Multiply,
                                    new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)a),
                                    new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)a + 2)),
                                new LiteralExpr(LiteralExpr.LiteralKind.Number, 2L)),
                            new LiteralExpr(LiteralExpr.LiteralKind.Number, 0L)),
                        new BlockStmt { Statements = { stmt } }
                    )
                },
                ElseBody = new BlockStmt
                {
                    Statements =
                    {
                        new LocalVarStmt
                        {
                            Names = { $"_{b:x8}" },
                            Values = { new LiteralExpr(LiteralExpr.LiteralKind.Nil, null) }
                        }
                    }
                }
            };
        }
        else
        {
            // Always-false: (a*a) < 0 (always false for integers)
            return new IfStmt
            {
                Branches =
                {
                    (
                        new BinaryExpr(BinaryOp.Lt,
                            new BinaryExpr(BinaryOp.Multiply,
                                new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)a),
                                new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)b)),
                            new LiteralExpr(LiteralExpr.LiteralKind.Number, 0L)),
                        new BlockStmt
                        {
                            Statements =
                            {
                                new LocalVarStmt
                                {
                                    Names = { $"_{b:x8}" },
                                    Values = { new LiteralExpr(LiteralExpr.LiteralKind.Nil, null) }
                                }
                            }
                        }
                    )
                },
                ElseBody = new BlockStmt { Statements = { stmt } }
            };
        }
    }

    private static Statement WrapInNestedOpaque(Statement stmt, RandomNumberGenerator rng)
    {
        var buf = new byte[8];
        rng.GetBytes(buf);
        var x = (BitConverter.ToInt32(buf, 0) & 0x7FFFFFFF) % 100 + 1;

        // Nested: if ((x+1)*(x+1) - 2*(x+1) + 1) == (x*x) then ... 
        // This is always true: (x+1)² - 2(x+1) + 1 = x² + 2x + 1 - 2x - 2 + 1 = x²
        return new IfStmt
        {
            Branches =
            {
                (
                    new BinaryExpr(BinaryOp.Eq,
                        new BinaryExpr(BinaryOp.Subtract,
                            new BinaryExpr(BinaryOp.Multiply,
                                new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)(x + 1)),
                                new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)(x + 1))),
                            new BinaryExpr(BinaryOp.Multiply,
                                new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)(2 * (x + 1))),
                                new LiteralExpr(LiteralExpr.LiteralKind.Number, 1L))),
                        new BinaryExpr(BinaryOp.Multiply,
                            new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)x),
                            new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)x))),
                    new BlockStmt { Statements = { stmt } }
                )
            },
            ElseBody = new BlockStmt
            {
                Statements =
                {
                    new LocalVarStmt
                    {
                        Names = { $"_{x:x8}" },
                        Values = { new LiteralExpr(LiteralExpr.LiteralKind.Nil, null) }
                    }
                }
            }
        };
    }
}