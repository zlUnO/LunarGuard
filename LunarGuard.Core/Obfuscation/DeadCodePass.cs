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

        var rng = new Random(Environment.TickCount);
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
        switch (rng.Next(8))
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
            default:
                return new LocalVarStmt
                {
                    Names = { $"_{rng.Next():x8}" },
                    Values = { MakeJunkExpr(rng) }
                };
        }
    }

    private static Expression MakeJunkExpr(Random rng)
    {
        return rng.Next(5) switch
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
            _ => new LiteralExpr(
                LiteralExpr.LiteralKind.String,
                $"junk_{rng.Next():x8}"
            )
        };
    }
}