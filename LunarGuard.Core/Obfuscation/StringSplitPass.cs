using LunarGuard.Core.AST;
using LunarGuard.Core.AST.Stmt;
using LunarGuard.Core.AST.Expr;

namespace LunarGuard.Core.Obfuscation;

public class StringSplitPass : IObfuscationPass
{
    public string Name => "String Splitting";

    public void Transform(BlockStmt root, ObfuscationOptions options)
    {
        if (!options.SplitStrings) return;
        var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        ProcessBlock(root, options, rng);
    }

    private static void ProcessBlock(BlockStmt block, ObfuscationOptions options, System.Security.Cryptography.RandomNumberGenerator rng)
    {
        var newStmts = new List<Statement>();
        foreach (var stmt in block.Statements)
        {
            switch (stmt)
            {
                case LocalVarStmt lv:
                    var replaced = new List<Expression>();
                    foreach (var v in lv.Values)
                    {
                        if (v is LiteralExpr l && l.Kind == LiteralExpr.LiteralKind.String)
                            replaced.Add(SplitString((string)l.Value!, options, rng, newStmts));
                        else
                            replaced.Add(v);
                    }
                    lv.Values.Clear();
                    lv.Values.AddRange(replaced);
                    break;

                case AssignmentStmt a:
                    for (var i = 0; i < a.Values.Count; i++)
                    {
                        if (a.Values[i] is LiteralExpr l && l.Kind == LiteralExpr.LiteralKind.String)
                            a.Values[i] = SplitString((string)l.Value!, options, rng, newStmts);
                    }
                    break;

                case ReturnStmt r:
                    for (var i = 0; i < r.Values.Count; i++)
                    {
                        if (r.Values[i] is LiteralExpr l && l.Kind == LiteralExpr.LiteralKind.String)
                            r.Values[i] = SplitString((string)l.Value!, options, rng, newStmts);
                    }
                    break;

                case FunctionCallStmt fc:
                    ProcessExpr(fc.Call, options, rng, newStmts);
                    break;

                case IfStmt iff:
                    foreach (var (cond, body) in iff.Branches)
                    {
                        ProcessBlock(body, options, rng);
                    }
                    if (iff.ElseBody != null) ProcessBlock(iff.ElseBody, options, rng);
                    break;

                case WhileStmt w:
                    ProcessBlock(w.Body, options, rng);
                    break;

                case RepeatStmt rep:
                    ProcessBlock(rep.Body, options, rng);
                    break;

                case ForNumericStmt fn:
                    ProcessBlock(fn.Body, options, rng);
                    break;

                case ForGenericStmt fg:
                    ProcessBlock(fg.Body, options, rng);
                    break;

                case DoStmt d:
                    ProcessBlock(d.Body, options, rng);
                    break;

                case FunctionDeclStmt fd:
                    ProcessBlock(fd.FuncExpr.Body, options, rng);
                    break;
            }
        }
        block.Statements.InsertRange(0, newStmts);
    }

    private static void ProcessExpr(Expression expr, ObfuscationOptions options, System.Security.Cryptography.RandomNumberGenerator rng, List<Statement> newStmts)
    {
        if (expr is FunctionCallExpr fc)
        {
            for (var i = 0; i < fc.Arguments.Count; i++)
            {
                if (fc.Arguments[i] is LiteralExpr l && l.Kind == LiteralExpr.LiteralKind.String)
                    fc.Arguments[i] = SplitString((string)l.Value!, options, rng, newStmts);
            }
        }
    }

    private static Expression SplitString(string s, ObfuscationOptions options, System.Security.Cryptography.RandomNumberGenerator rng, List<Statement> newStmts)
    {
        if (s.Length < options.StringSplitMinLen)
            return new LiteralExpr(LiteralExpr.LiteralKind.String, s);

        var buf = new byte[1];
        var parts = new List<string>();
        var pos = 0;
        while (pos < s.Length)
        {
            rng.GetBytes(buf);
            var chunkSize = Math.Max(1, buf[0] % 4 + 1);
            chunkSize = Math.Min(chunkSize, s.Length - pos);
            parts.Add(s.Substring(pos, chunkSize));
            pos += chunkSize;
        }

        if (parts.Count <= 1)
            return new LiteralExpr(LiteralExpr.LiteralKind.String, s);

        var tempVars = new List<string>();
        for (var i = 0; i < parts.Count; i++)
        {
            var varName = $"_ss_{NextHex(rng, 4)}";
            tempVars.Add(varName);
            newStmts.Add(new LocalVarStmt
            {
                Names = { varName },
                Values = { new LiteralExpr(LiteralExpr.LiteralKind.String, parts[i]) }
            });
        }

        var concat = new ConcatExpr();
        foreach (var tv in tempVars)
            concat.Parts.Add(new VarExpr(tv));
        return concat;
    }

    private static string NextHex(System.Security.Cryptography.RandomNumberGenerator rng, int bytes)
    {
        var buf = new byte[bytes];
        rng.GetBytes(buf);
        return Convert.ToHexString(buf).ToLower();
    }
}
