using System.Security.Cryptography;
using LunarGuard.Core.AST;
using LunarGuard.Core.AST.Stmt;
using LunarGuard.Core.AST.Expr;

namespace LunarGuard.Core.Obfuscation;

public class AntiDebugPass : IObfuscationPass
{
    public string Name => "Anti-Debug";

    public void Transform(BlockStmt root, ObfuscationOptions options)
    {
        if (!options.AntiDebug) return;

        using var rng = RandomNumberGenerator.Create();

        var checkPoints = FindCheckPoints(root, 3);
        foreach (var idx in checkPoints)
        {
            root.Statements.Insert(idx, CreateAntiDebugChunk(rng));
        }

        root.Statements.Insert(0, CreateAntiDebugChunk(rng));
    }

    private static List<int> FindCheckPoints(BlockStmt root, int count)
    {
        var points = new List<int>();
        for (var i = 0; i < root.Statements.Count; i++)
        {
            var s = root.Statements[i];
            if (s is FunctionDeclStmt) continue;
            if (s is LocalVarStmt lv && lv.Names.Count == 1 && lv.Names[0].Contains("_run")) continue;
            points.Add(i);
        }
        if (points.Count == 0) points.Add(0);

        var result = new List<int>();
        using var rng = RandomNumberGenerator.Create();
        for (var i = 0; i < count && points.Count > 0; i++)
        {
            var buf = new byte[4];
            rng.GetBytes(buf);
            var idx = buf[0] % points.Count;
            result.Add(points[idx]);
            points.RemoveAt(idx);
        }
        return result;
    }

    private static Statement CreateAntiDebugChunk(RandomNumberGenerator rng)
    {
        var buf = new byte[8];
        rng.GetBytes(buf);
        var checkType = buf[0] % 3;

        var chunkName = $"_{BitConverter.ToInt32(buf, 4):x8}";

        return checkType switch
        {
            0 => CreateTimingCheck(rng, chunkName),
            1 => CreateFunctionHookCheck(rng, chunkName),
            _ => CreatePcallCheck(rng, chunkName),
        };
    }

    private static Statement CreateTimingCheck(RandomNumberGenerator rng, string name)
    {
        var buf = new byte[4];
        rng.GetBytes(buf);

        var body = new BlockStmt();
        body.Statements.Add(new LocalVarStmt
        {
            Names = { $"t_{name}" },
            Values = { new FunctionCallExpr(new VarExpr("os.clock")) { Arguments = {} } }
        });
        body.Statements.Add(new LocalVarStmt
        {
            Names = { $"_{name}" },
            Values =
            {
                new BinaryExpr(BinaryOp.Gt,
                    new BinaryExpr(BinaryOp.Subtract,
                        new FunctionCallExpr(new VarExpr("os.clock")) { Arguments = {} },
                        new VarExpr($"t_{name}")),
                    new LiteralExpr(LiteralExpr.LiteralKind.Number, 1L))
            }
        });
        body.Statements.Add(new IfStmt
        {
            Branches =
            {
                (
                    new VarExpr($"_{name}"),
                    new BlockStmt
                    {
                        Statements =
                        {
                            new FunctionCallStmt
                            {
                                Call = new FunctionCallExpr(new VarExpr("error"))
                                {
                                    Arguments = { new LiteralExpr(LiteralExpr.LiteralKind.String, "timeout") }
                                }
                            }
                        }
                    }
                )
            }
        });

        return new DoStmt { Body = body };
    }

    private static Statement CreateFunctionHookCheck(RandomNumberGenerator rng, string name)
    {
        var buf = new byte[8];
        rng.GetBytes(buf);
        var checkVal = BitConverter.ToInt32(buf, 0) & 0x7FFFFF;

        var body = new BlockStmt();
        body.Statements.Add(new LocalVarStmt
        {
            Names = { $"f_{name}" },
            Values = { new VarExpr("print") }
        });
        body.Statements.Add(new IfStmt
        {
            Branches =
            {
                (
                    new BinaryExpr(BinaryOp.Neq,
                        new VarExpr($"f_{name}"),
                        new VarExpr("print")),
                    new BlockStmt
                    {
                        Statements =
                        {
                            new FunctionCallStmt
                            {
                                Call = new FunctionCallExpr(new VarExpr("error"))
                                {
                                    Arguments = { new LiteralExpr(LiteralExpr.LiteralKind.String, "hook") }
                                }
                            }
                        }
                    }
                )
            }
        });

        return new DoStmt { Body = body };
    }

    private static Statement CreatePcallCheck(RandomNumberGenerator rng, string name)
    {
        var buf = new byte[4];
        rng.GetBytes(buf);
        var flagVal = BitConverter.ToInt32(buf, 0) & 0xFFFFFF;

        var body = new BlockStmt();
        body.Statements.Add(new LocalVarStmt
        {
            Names = { $"ok_{name}", $"err_{name}" },
            Values =
            {
                new FunctionCallExpr(new MemberExpr(
                    new FunctionCallExpr(new VarExpr("pcall"))
                    {
                        Arguments =
                        {
                            new FuncDeclExpr
                            {
                                Body = new BlockStmt
                                {
                                    Statements =
                                    {
                                        new ReturnStmt
                                        {
                                            Values = { new LiteralExpr(LiteralExpr.LiteralKind.Boolean, true) }
                                        }
                                    }
                                }
                            }
                        }
                    }, "and"))
                {
                    Arguments = { new LiteralExpr(LiteralExpr.LiteralKind.Boolean, false) }
                }
            }
        });

        return new DoStmt { Body = body };
    }

    private static Statement CheckDebugExist(RandomNumberGenerator rng)
    {
        return new IfStmt
        {
            Branches =
            {
                (
                    new FunctionCallExpr(new VarExpr("pcall"))
                    {
                        Arguments =
                        {
                            new FuncDeclExpr
                            {
                                Body = new BlockStmt
                                {
                                    Statements =
                                    {
                                        new LocalVarStmt
                                        {
                                            Names = { "d" },
                                            Values = { new VarExpr("debug") }
                                        }
                                    }
                                }
                            }
                        }
                    },
                    new BlockStmt()
                )
            }
        };
    }
}
