using System.Security.Cryptography;
using LunarGuard.Core.AST;
using LunarGuard.Core.AST.Stmt;
using LunarGuard.Core.AST.Expr;

namespace LunarGuard.Core.Obfuscation;

public class AntiTamperPass : IObfuscationPass
{
    public string Name => "Anti-Tamper";

    public void Transform(BlockStmt root, ObfuscationOptions options)
    {
        if (!options.AntiTamper) return;
        using var rng = RandomNumberGenerator.Create();

        // Embed related constant checks at 2-3 random points in the root.
        // Each check: if a * 7 + 13 ~= expected_a then error("integrity")
        // This makes it harder for attackers to patch all checks.
        var points = FindInsertPoints(root, 3);
        for (var i = 0; i < points.Count; i++)
        {
            var buf = new byte[8];
            rng.GetBytes(buf);
            var seed = (BitConverter.ToInt64(buf, 0) & 0x7FFFFFFF) + 100;
            if (seed < 0) seed = -seed;
            var prefix = $"_at_{NextHex(rng, 4)}";
            var varName = $"{prefix}_v";
            var derived = seed * 7 + 13;

            // local <varName> = <seed>
            // if <varName> * 7 + 13 ~= <derived> then error("integrity") end
            var checkBlock = new BlockStmt();

            checkBlock.Statements.Add(new LocalVarStmt
            {
                Names = { varName },
                Values = { new LiteralExpr(LiteralExpr.LiteralKind.Number, seed) }
            });

            checkBlock.Statements.Add(new IfStmt
            {
                Branches =
                {
                    (
                        new BinaryExpr(BinaryOp.Neq,
                            new BinaryExpr(BinaryOp.Add,
                                new BinaryExpr(BinaryOp.Multiply, new VarExpr(varName),
                                    new LiteralExpr(LiteralExpr.LiteralKind.Number, 7L)),
                                new LiteralExpr(LiteralExpr.LiteralKind.Number, 13L)),
                            new LiteralExpr(LiteralExpr.LiteralKind.Number, derived)),
                        new BlockStmt
                        {
                            Statements =
                            {
                                new FunctionCallStmt
                                {
                                    Call = new FunctionCallExpr(new VarExpr("error"))
                                    {
                                        Arguments =
                                        {
                                            new LiteralExpr(LiteralExpr.LiteralKind.String,
                                                "code integrity check failed")
                                        }
                                    }
                                }
                            }
                        }
                    )
                }
            });

            root.Statements.Insert(points[i], checkBlock);
        }
    }

    private static List<int> FindInsertPoints(BlockStmt root, int count)
    {
        var points = new List<int>();
        for (var i = 1; i < root.Statements.Count; i++)
        {
            if (root.Statements[i] is not (FunctionDeclStmt or LocalVarStmt or DoStmt or FunctionCallStmt))
                points.Add(i);
            if (points.Count >= count * 5) break;
        }
        if (points.Count == 0) points.Add(1);

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

    private static string NextHex(RandomNumberGenerator rng, int bytes)
    {
        var buf = new byte[bytes];
        rng.GetBytes(buf);
        return Convert.ToHexString(buf).ToLower();
    }
}
