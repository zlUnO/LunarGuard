using System.Security.Cryptography;
using LunarGuard.Core.AST;
using LunarGuard.Core.AST.Stmt;
using LunarGuard.Core.AST.Expr;

namespace LunarGuard.Core.Obfuscation;

public class DeadCodePass : IObfuscationPass
{
    public string Name => "Dead Code Injection";

    public void Transform(BlockStmt root, ObfuscationOptions options)
    {
        if (!options.InjectDeadCode) return;

        var rng = new Random(Environment.TickCount ^ Environment.CurrentManagedThreadId);
        Inject(root, options.DeadCodeBlocks, rng);
    }

    private static void Inject(BlockStmt block, int count, Random rng)
    {
        var original = block.Statements.ToList();
        var insertPoints = new List<int>();

        for (var i = 0; i < original.Count; i++)
            if (original[i] is not ReturnStmt and not FunctionDeclStmt
                and not BreakStmt and not GotoStmt and not LabelStmt)
                insertPoints.Add(i);

        if (insertPoints.Count > 0)
        {
            for (var n = 0; n < count; n++)
            {
                var pos = insertPoints[rng.Next(insertPoints.Count)];
                var junk = GenerateJunk(rng);
                block.Statements.Insert(pos, junk);
            }
        }

        foreach (var stmt in block.Statements.ToList())
            InjectIntoNested(stmt, count, rng);
    }

    private static void InjectIntoNested(Statement stmt, int count, Random rng)
    {
        switch (stmt)
        {
            case IfStmt i:
                foreach (var (_, b) in i.Branches) Inject(b, Math.Max(1, count / 2), rng);
                if (i.ElseBody != null) Inject(i.ElseBody, Math.Max(1, count / 2), rng);
                break;
            case WhileStmt w:
                Inject(w.Body, Math.Max(1, count / 2), rng);
                break;
            case RepeatStmt r:
                Inject(r.Body, Math.Max(1, count / 2), rng);
                break;
            case DoStmt d:
                Inject(d.Body, Math.Max(1, count / 2), rng);
                break;
            case ForNumericStmt fn:
                Inject(fn.Body, Math.Max(1, count / 2), rng);
                break;
            case ForGenericStmt fg:
                Inject(fg.Body, Math.Max(1, count / 2), rng);
                break;
            case FunctionDeclStmt fd:
                Inject(fd.FuncExpr.Body, Math.Max(1, count / 2), rng);
                break;
        }
    }

    private static Statement GenerateJunk(Random rng)
    {
        var variant = rng.Next(12);

        switch (variant)
        {
            case 0:
            {
                var name = $"_{rng.Next():x8}";
                return new LocalVarStmt
                {
                    Names = { name },
                    Values = { MakeJunkExpr(rng) }
                };
            }
            case 1:
            {
                var name = $"_{rng.Next():x8}";
                return new LocalVarStmt
                {
                    Names = { name, $"_{rng.Next():x8}" },
                    Values = { MakeJunkExpr(rng), MakeJunkExpr(rng) }
                };
            }
            case 2:
                return new DoStmt
                {
                    Body = new BlockStmt
                    {
                        Statements =
                        {
                            new LocalVarStmt
                            {
                                Names = { $"_{rng.Next():x8}" },
                                Values = { MakeJunkExpr(rng) }
                            }
                        }
                    }
                };
            case 3:
                return new IfStmt
                {
                    Branches =
                    {
                        (new LiteralExpr(LiteralExpr.LiteralKind.Boolean, false),
                         new BlockStmt
                         {
                             Statements =
                             {
                                 new LocalVarStmt
                                 {
                                     Names = { $"_{rng.Next():x8}" },
                                     Values = { MakeJunkExpr(rng) }
                                 }
                             }
                         })
                    }
                };
            case 4:
            {
                var name = $"_{rng.Next():x8}";
                return new LocalVarStmt
                {
                    Names = { name },
                    Values = { new LiteralExpr(LiteralExpr.LiteralKind.Nil, null) }
                };
            }
            case 5:
                return new DoStmt
                {
                    Body = new BlockStmt
                    {
                        Statements =
                        {
                            new IfStmt
                            {
                                Branches =
                                {
                                    (new LiteralExpr(LiteralExpr.LiteralKind.Boolean, true),
                                     new BlockStmt())
                                }
                            }
                        }
                    }
                };
            case 6:
            {
                var name = $"_{rng.Next():x8}";
                return new AssignmentStmt
                {
                    Targets = { new VarExpr(name) },
                    Values = { MakeJunkExpr(rng) }
                };
            }
            case 7:
                return new WhileStmt
                {
                    Condition = new LiteralExpr(LiteralExpr.LiteralKind.Boolean, false),
                    Body = new BlockStmt
                    {
                        Statements =
                        {
                            new FunctionCallStmt
                            {
                                Call = new FunctionCallExpr(new VarExpr("print"))
                                {
                                    Arguments = { new LiteralExpr(LiteralExpr.LiteralKind.String, $"_{rng.Next():x8}") }
                                }
                            }
                        }
                    }
                };
            case 8:
                return new RepeatStmt
                {
                    Condition = new LiteralExpr(LiteralExpr.LiteralKind.Boolean, true),
                    Body = new BlockStmt
                    {
                        Statements =
                        {
                            new LocalVarStmt
                            {
                                Names = { $"_{rng.Next():x8}" },
                                Values = { MakeJunkExpr(rng) }
                            }
                        }
                    }
                };
            case 9:
            {
                var name = $"_{rng.Next():x8}";
                var val = rng.Next(1, 999);
                return new LocalVarStmt
                {
                    Names = { name },
                    Values =
                    {
                        new BinaryExpr(
                            BinaryOp.Eq,
                            new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)val),
                            new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)val))
                    }
                };
            }
            case 10:
                return new IfStmt
                {
                    Branches =
                    {
                        (MakeJunkExpr(rng),
                         new BlockStmt())
                    },
                    ElseBody = new BlockStmt
                    {
                        Statements =
                        {
                            new LocalVarStmt
                            {
                                Names = { $"_{rng.Next():x8}" },
                                Values = { MakeJunkExpr(rng) }
                            }
                        }
                    }
                };
            default:
            {
                var name = $"_{rng.Next():x8}";
                return new LocalVarStmt
                {
                    Names = { name },
                    Values = { MakeJunkExpr(rng) }
                };
            }
        }
    }

    private static Expression MakeJunkExpr(Random rng)
    {
        return rng.Next(8) switch
        {
            0 => new BinaryExpr(
                BinaryOp.Add,
                new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)rng.Next(1, 999)),
                new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)rng.Next(1, 999))
            ),
            1 => new BinaryExpr(
                BinaryOp.Multiply,
                new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)rng.Next(2, 50)),
                new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)rng.Next(2, 50))
            ),
            2 => new UnaryExpr(
                UnaryOp.Not,
                new LiteralExpr(LiteralExpr.LiteralKind.Boolean, rng.Next(2) == 0)
            ),
            3 => new BinaryExpr(
                BinaryOp.Concat,
                new LiteralExpr(LiteralExpr.LiteralKind.String, $"junk_{rng.Next():x8}"),
                new LiteralExpr(LiteralExpr.LiteralKind.String, $"_{rng.Next():x4}")
            ),
            4 => new BinaryExpr(
                BinaryOp.Modulo,
                new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)rng.Next(10, 1000)),
                new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)rng.Next(2, 99))
            ),
            5 => new BinaryExpr(
                BinaryOp.Subtract,
                new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)rng.Next(500, 999)),
                new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)rng.Next(1, 499))
            ),
            6 => new FunctionCallExpr(new VarExpr("tostring"))
            {
                Arguments = { new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)rng.Next(1, 9999)) }
            },
            _ => new LiteralExpr(
                LiteralExpr.LiteralKind.String,
                $"junk_{rng.Next():x8}"
            )
        };
    }
}
