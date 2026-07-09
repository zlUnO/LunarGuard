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

        // Scatter multiple small anti-debug checks throughout the code
        // These are in addition to what's integrated into the VM
        var checkPoints = FindCheckPoints(root, 3);
        foreach (var idx in checkPoints)
        {
            root.Statements.Insert(idx, CreateAntiDebugChunk(rng));
        }

        // Also add one at the very beginning
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
        var falseTarget = BitConverter.ToInt32(buf, 0) & 0x7FFFFFFF;
        var branchVar = $"_{BitConverter.ToInt32(buf, 4):x8}";

        // Wrap in pcall to avoid crashes when `debug` is nil (GameSense sandbox)
        // pcall(function() if debug.getinfo(0) == nil then os.exit(1) end end)
        var pcallBody = new BlockStmt();
        pcallBody.Statements.Add(new IfStmt
        {
            Branches =
            {
                (
                    new BinaryExpr(BinaryOp.Eq,
                        new FunctionCallExpr(new MemberExpr(new VarExpr("debug"), "getinfo"))
                        {
                            Arguments = { new LiteralExpr(LiteralExpr.LiteralKind.Number, 0L) }
                        },
                        new LiteralExpr(LiteralExpr.LiteralKind.Nil, null)),
                    new BlockStmt
                    {
                        Statements =
                        {
                            new FunctionCallStmt
                            {
                                Call = new FunctionCallExpr(new MemberExpr(new VarExpr("os"), "exit"))
                                {
                                    Arguments = { new LiteralExpr(LiteralExpr.LiteralKind.Number, 1L) }
                                }
                            }
                        }
                    }
                )
            }
        });

        var pcallFunc = new FuncDeclExpr { Parameters = { }, Body = pcallBody };
        return new FunctionCallStmt
        {
            Call = new FunctionCallExpr(new VarExpr("pcall"))
            {
                Arguments = { pcallFunc }
            }
        };
    }
}