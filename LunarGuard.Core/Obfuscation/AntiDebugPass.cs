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

        root.Statements.Insert(0, CreateDebugCheck());
        root.Statements.Insert(0, CreateDetourCheck());
    }

    private static Statement CreateDetourCheck()
    {
        var cond1 = new BinaryExpr(BinaryOp.Eq,
            new FunctionCallExpr(new MemberExpr(new VarExpr("debug"), "getinfo"))
            {
                Arguments = { new LiteralExpr(LiteralExpr.LiteralKind.Number, 0L) }
            },
            new LiteralExpr(LiteralExpr.LiteralKind.Nil, null));

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
                            (
                                cond1,
                                new BlockStmt
                                {
                                    Statements =
                                    {
                                        new ReturnStmt
                                        {
                                            Values =
                                            {
                                                new FunctionCallExpr(new VarExpr("error"))
                                                {
                                                    Arguments =
                                                    {
                                                        new LiteralExpr(LiteralExpr.LiteralKind.String, "debugger detected"),
                                                        new LiteralExpr(LiteralExpr.LiteralKind.Number, 0L)
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            )
                        }
                    }
                }
            }
        };
    }

    private static Statement CreateDebugCheck()
    {
        var dbgExists = new BinaryExpr(BinaryOp.Neq,
            new VarExpr("debug"), new LiteralExpr(LiteralExpr.LiteralKind.Nil, null));
        var getinfoExists = new BinaryExpr(BinaryOp.And, dbgExists,
            new BinaryExpr(BinaryOp.Neq,
                new MemberExpr(new VarExpr("debug"), "getinfo"),
                new LiteralExpr(LiteralExpr.LiteralKind.Nil, null)));
        var tracebackExists = new BinaryExpr(BinaryOp.And, getinfoExists,
            new BinaryExpr(BinaryOp.Neq,
                new MemberExpr(new VarExpr("debug"), "traceback"),
                new LiteralExpr(LiteralExpr.LiteralKind.Nil, null)));
        var getregistryExists = new BinaryExpr(BinaryOp.And, tracebackExists,
            new BinaryExpr(BinaryOp.Neq,
                new MemberExpr(new VarExpr("debug"), "getregistry"),
                new LiteralExpr(LiteralExpr.LiteralKind.Nil, null)));

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
                            (
                                getregistryExists,
                                new BlockStmt
                                {
                                    Statements =
                                    {
                                        new ReturnStmt
                                        {
                                            Values =
                                            {
                                                new FunctionCallExpr(new VarExpr("error"))
                                                {
                                                    Arguments =
                                                    {
                                                        new LiteralExpr(LiteralExpr.LiteralKind.String, "debugger detected"),
                                                        new LiteralExpr(LiteralExpr.LiteralKind.Number, 0L)
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            )
                        }
                    }
                }
            }
        };
    }
}