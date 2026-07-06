using System.Security.Cryptography;
using LunarGuard.Core.AST;
using LunarGuard.Core.AST.Stmt;
using LunarGuard.Core.AST.Expr;

namespace LunarGuard.Core.Obfuscation;

public class ExpressionSplitPass : IObfuscationPass
{
    public string Name => "Expression Splitting";

    public void Transform(BlockStmt root, ObfuscationOptions options)
    {
        if (!options.SplitExpressions) return;

        var seedBytes = new byte[4];
        RandomNumberGenerator.Fill(seedBytes);
        var rng = new Random(BitConverter.ToInt32(seedBytes, 0) & 0x7FFFFFFF);
        ProcessBlock(root, rng);
    }

    private static void ProcessBlock(BlockStmt block, Random rng)
    {
        var newStmts = new List<Statement>();
        foreach (var stmt in block.Statements)
            ProcessStmt(stmt, newStmts, rng);
        block.Statements.Clear();
        block.Statements.AddRange(newStmts);
    }

    private static void ProcessStmt(Statement stmt, List<Statement> result, Random rng)
    {
        var usedNames = new HashSet<string>();
        var beforeStmt = new List<Statement>();

        switch (stmt)
        {
            case LocalVarStmt l:
                foreach (var n in l.Names) usedNames.Add(n);
                for (var i = 0; i < l.Values.Count; i++)
                {
                    if (i == l.Values.Count - 1 && l.Values.Count <= l.Names.Count
                        && l.Values[i] is FunctionCallExpr) continue;
                    l.Values[i] = SplitExpr(l.Values[i], beforeStmt, rng, usedNames);
                }
                break;
            case AssignmentStmt a:
                for (var i = 0; i < a.Values.Count; i++)
                {
                    if (i == a.Values.Count - 1 && a.Values.Count <= a.Targets.Count
                        && a.Values[i] is FunctionCallExpr) continue;
                    a.Values[i] = SplitExpr(a.Values[i], beforeStmt, rng, usedNames);
                }
                break;
            case IfStmt I:
                for (var i = 0; i < I.Branches.Count; i++)
                {
                    var (c, b) = I.Branches[i];
                    var beforeCond = new List<Statement>();
                    I.Branches[i] = (SplitExpr(c, beforeCond, rng, usedNames), b);
                    beforeStmt.AddRange(beforeCond);
                    ProcessBlock(b, rng);
                }
                if (I.ElseBody != null) ProcessBlock(I.ElseBody, rng);
                break;
            case WhileStmt w:
                var beforeWhile = new List<Statement>();
                w.Condition = SplitExpr(w.Condition, beforeWhile, rng, usedNames);
                beforeStmt.AddRange(beforeWhile);
                ProcessBlock(w.Body, rng);
                break;
            case RepeatStmt r:
                ProcessBlock(r.Body, rng);
                var beforeRepeat = new List<Statement>();
                r.Condition = SplitExpr(r.Condition, beforeRepeat, rng, usedNames);
                beforeStmt.AddRange(beforeRepeat);
                break;
            case DoStmt d:
                ProcessBlock(d.Body, rng);
                break;
            case ForNumericStmt fn:
                var beforeFor = new List<Statement>();
                fn.Start = SplitExpr(fn.Start, beforeFor, rng, usedNames);
                fn.End = SplitExpr(fn.End, beforeFor, rng, usedNames);
                if (fn.Step != null) fn.Step = SplitExpr(fn.Step, beforeFor, rng, usedNames);
                beforeStmt.AddRange(beforeFor);
                ProcessBlock(fn.Body, rng);
                break;
            case ForGenericStmt fg:
                var beforeGen = new List<Statement>();
                for (var i = 0; i < fg.Iterators.Count; i++)
                    fg.Iterators[i] = SplitExpr(fg.Iterators[i], beforeGen, rng, usedNames);
                beforeStmt.AddRange(beforeGen);
                ProcessBlock(fg.Body, rng);
                break;
            case ReturnStmt rs:
                for (var i = 0; i < rs.Values.Count; i++)
                {
                    if (i == rs.Values.Count - 1 && rs.Values[i] is FunctionCallExpr) continue;
                    rs.Values[i] = SplitExpr(rs.Values[i], beforeStmt, rng, usedNames);
                }
                break;
            case FunctionCallStmt fc:
                var beforeCall = new List<Statement>();
                SplitFuncCallExpr(fc.Call, beforeCall, rng, usedNames);
                beforeStmt.AddRange(beforeCall);
                break;
            case FunctionDeclStmt fd:
                ProcessBlock(fd.FuncExpr.Body, rng);
                break;
        }

        result.AddRange(beforeStmt);
        result.Add(stmt);
    }

    private static Expression SplitExpr(Expression expr, List<Statement> beforeStmt, Random rng, HashSet<string> usedNames)
    {
        if (expr is LiteralExpr or VarExpr or VarargsExpr)
            return expr;

        if (rng.NextDouble() < 0.3)
        {
            var tempName = UniqueName(usedNames, rng);
            beforeStmt.Add(new LocalVarStmt
            {
                Names = { tempName },
                Values = { expr }
            });
            return new VarExpr(tempName);
        }

        if (expr is BinaryExpr b)
        {
            var newLeft = SplitExpr(b.Left, beforeStmt, rng, usedNames);
            var newRight = SplitExpr(b.Right, beforeStmt, rng, usedNames);
            if (newLeft != b.Left || newRight != b.Right)
                return new BinaryExpr(b.Op, newLeft, newRight);
        }
        else if (expr is UnaryExpr u)
        {
            var newOp = SplitExpr(u.Operand, beforeStmt, rng, usedNames);
            if (newOp != u.Operand)
                return new UnaryExpr(u.Op, newOp);
        }

        return expr;
    }

    private static void SplitFuncCallExpr(FunctionCallExpr fc, List<Statement> beforeStmt, Random rng, HashSet<string> usedNames)
    {
        if (fc.Arguments.Count == 1 && fc.Arguments[0] is LiteralExpr { Kind: LiteralExpr.LiteralKind.String })
            return;
        for (var i = 0; i < fc.Arguments.Count; i++)
            fc.Arguments[i] = SplitExpr(fc.Arguments[i], beforeStmt, rng, usedNames);
    }

    private static string UniqueName(HashSet<string> used, Random rng)
    {
        string name;
        do { name = $"_{Math.Abs(rng.Next()):x8}"; }
        while (!used.Add(name));
        return name;
    }
}