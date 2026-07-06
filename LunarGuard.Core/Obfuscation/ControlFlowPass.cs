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

        var shuffled = insertPoints.OrderBy(_ => NextByte(rng)).ToList();

        var wrapCount = Math.Max(1, shuffled.Count / 3);
        for (var i = 0; i < wrapCount && i < shuffled.Count; i++)
        {
            var idx = shuffled[i];
            var stmt = stmts[idx];
            if (stmt is not LocalVarStmt && (stmt is AssignmentStmt or FunctionCallStmt or IfStmt or WhileStmt or DoStmt))
            {
                stmts[idx] = WrapInOpaquePredicate(stmt, rng);
            }
        }

        block.Statements.Clear();
        block.Statements.AddRange(stmts);
    }

    private static byte NextByte(RandomNumberGenerator rng)
    {
        var buf = new byte[1];
        rng.GetBytes(buf);
        return buf[0];
    }

    private static int NextInt32(RandomNumberGenerator rng, int max)
    {
        var buf = new byte[4];
        rng.GetBytes(buf);
        return Math.Abs(BitConverter.ToInt32(buf, 0)) % max;
    }

    private static long NextLong(RandomNumberGenerator rng, long max)
    {
        var buf = new byte[8];
        rng.GetBytes(buf);
        return Math.Abs(BitConverter.ToInt64(buf, 0)) % max;
    }

    private static Statement WrapInOpaquePredicate(Statement stmt, RandomNumberGenerator rng)
    {
        var variant = NextInt32(rng, 8);
        return variant switch
        {
            0 => OpaqueConsecutiveProduct(stmt, rng),
            1 => OpaqueCubicMinusSelf(stmt, rng),
            2 => OpaqueOddSquareMod8(stmt, rng),
            3 => OpaqueDistributiveIdentity(stmt, rng),
            4 => OpaqueDifferenceOfSquares(stmt, rng),
            5 => OpaqueSquareAlwaysPositive(stmt, rng),
            6 => OpaqueCompoundPredicate(stmt, rng),
            _ => OpaqueDoubleNegation(stmt, rng),
        };
    }

    private static Expression MakeConst(long val) =>
        new LiteralExpr(LiteralExpr.LiteralKind.Number, val);

    private static Expression MakeVar(string name) =>
        new VarExpr(name);

    /// <summary>Always-true: a * (a + 1) % 2 == 0 (product of consecutive ints is always even)</summary>
    private static Statement OpaqueConsecutiveProduct(Statement stmt, RandomNumberGenerator rng)
    {
        var a = NextLong(rng, 10000) + 1;
        var unusedName = $"_{NextInt32(rng, int.MaxValue):x8}";

        return new IfStmt
        {
            Branches =
            {
                (
                    new BinaryExpr(BinaryOp.Eq,
                        new BinaryExpr(BinaryOp.Modulo,
                            new BinaryExpr(BinaryOp.Multiply,
                                MakeConst(a), MakeConst(a + 1)),
                            MakeConst(2)),
                        MakeConst(0)),
                    new BlockStmt { Statements = { stmt } }
                )
            },
            ElseBody = new BlockStmt
            {
                Statements =
                {
                    new LocalVarStmt
                    {
                        Names = { unusedName },
                        Values = { new LiteralExpr(LiteralExpr.LiteralKind.Nil, null) }
                    }
                }
            }
        };
    }

    /// <summary>Always-true: (a³ - a) % 6 == 0 (product of 3 consecutive integers is always divisible by 6)</summary>
    private static Statement OpaqueCubicMinusSelf(Statement stmt, RandomNumberGenerator rng)
    {
        var a = NextLong(rng, 500) + 1;
        var unusedName = $"_{NextInt32(rng, int.MaxValue):x8}";

        return new IfStmt
        {
            Branches =
            {
                (
                    new BinaryExpr(BinaryOp.Eq,
                        new BinaryExpr(BinaryOp.Modulo,
                            new BinaryExpr(BinaryOp.Subtract,
                                new BinaryExpr(BinaryOp.Multiply,
                                    new BinaryExpr(BinaryOp.Multiply,
                                        MakeConst(a), MakeConst(a)),
                                    MakeConst(a)),
                                MakeConst(a)),
                            MakeConst(6)),
                        MakeConst(0)),
                    new BlockStmt { Statements = { stmt } }
                )
            },
            ElseBody = new BlockStmt
            {
                Statements =
                {
                    new LocalVarStmt
                    {
                        Names = { unusedName },
                        Values = { new LiteralExpr(LiteralExpr.LiteralKind.Nil, null) }
                    }
                }
            }
        };
    }

    /// <summary>Always-true: (2a+1)² % 8 == 1 (any odd number squared ≡ 1 mod 8)</summary>
    private static Statement OpaqueOddSquareMod8(Statement stmt, RandomNumberGenerator rng)
    {
        var a = NextLong(rng, 1000) + 1;
        var odd = 2 * a + 1;
        var unusedName = $"_{NextInt32(rng, int.MaxValue):x8}";

        return new IfStmt
        {
            Branches =
            {
                (
                    new BinaryExpr(BinaryOp.Eq,
                        new BinaryExpr(BinaryOp.Modulo,
                            new BinaryExpr(BinaryOp.Multiply,
                                MakeConst(odd), MakeConst(odd)),
                            MakeConst(8)),
                        MakeConst(1)),
                    new BlockStmt { Statements = { stmt } }
                )
            },
            ElseBody = new BlockStmt
            {
                Statements =
                {
                    new LocalVarStmt
                    {
                        Names = { unusedName },
                        Values = { new LiteralExpr(LiteralExpr.LiteralKind.Nil, null) }
                    }
                }
            }
        };
    }

    /// <summary>Always-true: (a+b)² - 2ab == a² + b² (algebraic identity)</summary>
    private static Statement OpaqueDistributiveIdentity(Statement stmt, RandomNumberGenerator rng)
    {
        var a = NextLong(rng, 500) + 1;
        var b = NextLong(rng, 500) + 1;
        var unusedName = $"_{NextInt32(rng, int.MaxValue):x8}";

        var leftSum = new BinaryExpr(BinaryOp.Add, MakeConst(a), MakeConst(b));

        return new IfStmt
        {
            Branches =
            {
                (
                    new BinaryExpr(BinaryOp.Eq,
                        new BinaryExpr(BinaryOp.Subtract,
                            new BinaryExpr(BinaryOp.Multiply, leftSum, leftSum),
                            new BinaryExpr(BinaryOp.Multiply,
                                MakeConst(2),
                                new BinaryExpr(BinaryOp.Multiply, MakeConst(a), MakeConst(b)))),
                        new BinaryExpr(BinaryOp.Add,
                            new BinaryExpr(BinaryOp.Multiply, MakeConst(a), MakeConst(a)),
                            new BinaryExpr(BinaryOp.Multiply, MakeConst(b), MakeConst(b)))),
                    new BlockStmt { Statements = { stmt } }
                )
            },
            ElseBody = new BlockStmt
            {
                Statements =
                {
                    new LocalVarStmt
                    {
                        Names = { unusedName },
                        Values = { new LiteralExpr(LiteralExpr.LiteralKind.Nil, null) }
                    }
                }
            }
        };
    }

    /// <summary>Always-true: (a+b)(a-b) == a² - b² (difference of squares)</summary>
    private static Statement OpaqueDifferenceOfSquares(Statement stmt, RandomNumberGenerator rng)
    {
        var aVal = NextLong(rng, 500) + 10;
        var bVal = NextLong(rng, 500) + 1;
        if (bVal >= aVal) bVal = aVal - 1;
        if (bVal < 1) bVal = 1;
        var unusedName = $"_{NextInt32(rng, int.MaxValue):x8}";

        var leftSum = new BinaryExpr(BinaryOp.Add, MakeConst(aVal), MakeConst(bVal));
        var leftDiff = new BinaryExpr(BinaryOp.Subtract, MakeConst(aVal), MakeConst(bVal));

        return new IfStmt
        {
            Branches =
            {
                (
                    new BinaryExpr(BinaryOp.Eq,
                        new BinaryExpr(BinaryOp.Multiply, leftSum, leftDiff),
                        new BinaryExpr(BinaryOp.Subtract,
                            new BinaryExpr(BinaryOp.Multiply, MakeConst(aVal), MakeConst(aVal)),
                            new BinaryExpr(BinaryOp.Multiply, MakeConst(bVal), MakeConst(bVal)))),
                    new BlockStmt { Statements = { stmt } }
                )
            },
            ElseBody = new BlockStmt
            {
                Statements =
                {
                    new LocalVarStmt
                    {
                        Names = { unusedName },
                        Values = { new LiteralExpr(LiteralExpr.LiteralKind.Nil, null) }
                    }
                }
            }
        };
    }

    /// <summary>Always-false: a*a < 0 (square of any real is non-negative)</summary>
    private static Statement OpaqueSquareAlwaysPositive(Statement stmt, RandomNumberGenerator rng)
    {
        var a = NextLong(rng, 10000) + 1;
        var unusedName = $"_{NextInt32(rng, int.MaxValue):x8}";

        return new IfStmt
        {
            Branches =
            {
                (
                    new BinaryExpr(BinaryOp.Lt,
                        new BinaryExpr(BinaryOp.Multiply, MakeConst(a), MakeConst(a)),
                        MakeConst(0)),
                    new BlockStmt
                    {
                        Statements =
                        {
                            new LocalVarStmt
                            {
                                Names = { unusedName },
                                Values = { new LiteralExpr(LiteralExpr.LiteralKind.Nil, null) }
                            }
                        }
                    }
                )
            },
            ElseBody = new BlockStmt { Statements = { stmt } }
        };
    }

    /// <summary>Always-true via compound: (a*b >= 0) or (a < 0 and b < 0)</summary>
    private static Statement OpaqueCompoundPredicate(Statement stmt, RandomNumberGenerator rng)
    {
        var a = NextLong(rng, 10000) + 1;
        var b = NextLong(rng, 10000) + 1;
        var unusedName = $"_{NextInt32(rng, int.MaxValue):x8}";

        var cond = new BinaryExpr(BinaryOp.Or,
            new BinaryExpr(BinaryOp.Geq,
                new BinaryExpr(BinaryOp.Multiply, MakeConst(a), MakeConst(b)),
                MakeConst(0)),
            new BinaryExpr(BinaryOp.And,
                new BinaryExpr(BinaryOp.Lt, MakeConst(a), MakeConst(0)),
                new BinaryExpr(BinaryOp.Lt, MakeConst(b), MakeConst(0))));

        return new IfStmt
        {
            Branches =
            {
                (cond, new BlockStmt { Statements = { stmt } })
            },
            ElseBody = new BlockStmt
            {
                Statements =
                {
                    new LocalVarStmt
                    {
                        Names = { unusedName },
                        Values = { new LiteralExpr(LiteralExpr.LiteralKind.Nil, null) }
                    }
                }
            }
        };
    }

    /// <summary>Always-true: not not (a == a) — double negation tautology wrapped in do</summary>
    private static Statement OpaqueDoubleNegation(Statement stmt, RandomNumberGenerator rng)
    {
        var a = NextLong(rng, 10000) + 1;

        var inner = new IfStmt
        {
            Branches =
            {
                (
                    new UnaryExpr(UnaryOp.Not,
                        new UnaryExpr(UnaryOp.Not,
                            new BinaryExpr(BinaryOp.Eq, MakeConst(a), MakeConst(a)))),
                    new BlockStmt { Statements = { stmt } }
                )
            }
        };

        return new DoStmt
        {
            Body = new BlockStmt
            {
                Statements = { inner }
            }
        };
    }
}