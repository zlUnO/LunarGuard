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

        var rng = new Random(1337);
        ProcessBlock(root, rng);
    }

    private static void ProcessBlock(BlockStmt block, Random rng)
    {
        var toInsert = new List<Statement>();
        ProcessStatements(block.Statements, toInsert, rng);
        if (toInsert.Count > 0)
            block.Statements.InsertRange(0, toInsert);
    }

    private static void ProcessStatements(List<Statement> stmts, List<Statement> toInsert, Random rng)
    {
        foreach (var stmt in stmts.ToList())
            ProcessStmt(stmt, toInsert, rng);
    }

    private static string UniqueName(HashSet<string> used, Random rng)
    {
        string name;
        do { name = $"_{Math.Abs(rng.Next()):x8}"; }
        while (!used.Add(name));
        return name;
    }

    private static void ProcessStmt(Statement stmt, List<Statement> toInsert, Random rng)
    {
        var usedNames = new HashSet<string>();

        switch (stmt)
        {
            case LocalVarStmt l:
                foreach (var n in l.Names) usedNames.Add(n);
                for (var i = 0; i < l.Values.Count; i++)
                {
                    if (i == l.Values.Count - 1 && l.Values.Count <= l.Names.Count
                        && l.Values[i] is FunctionCallExpr) continue;
                    l.Values[i] = SplitExpr(l.Values[i], toInsert, rng, usedNames);
                }
                break;
            case AssignmentStmt a:
                for (var i = 0; i < a.Values.Count; i++)
                {
                    if (i == a.Values.Count - 1 && a.Values.Count <= a.Targets.Count
                        && a.Values[i] is FunctionCallExpr) continue;
                    a.Values[i] = SplitExpr(a.Values[i], toInsert, rng, usedNames);
                }
                break;
            case IfStmt I:
                for (var i = 0; i < I.Branches.Count; i++)
                {
                    var (c, b) = I.Branches[i];
                    I.Branches[i] = (SplitExpr(c, toInsert, rng, usedNames), b);
                    ProcessBlock(b, rng);
                }
                if (I.ElseBody != null) ProcessBlock(I.ElseBody, rng);
                break;
            case WhileStmt w:
                w.Condition = SplitExpr(w.Condition, toInsert, rng, usedNames);
                ProcessBlock(w.Body, rng);
                break;
            case RepeatStmt r:
                ProcessBlock(r.Body, rng);
                r.Condition = SplitExpr(r.Condition, toInsert, rng, usedNames);
                break;
            case DoStmt d:
                ProcessBlock(d.Body, rng);
                break;
            case ForNumericStmt fn:
                fn.Start = SplitExpr(fn.Start, toInsert, rng, usedNames);
                fn.End = SplitExpr(fn.End, toInsert, rng, usedNames);
                if (fn.Step != null) fn.Step = SplitExpr(fn.Step, toInsert, rng, usedNames);
                ProcessBlock(fn.Body, rng);
                break;
            case ForGenericStmt fg:
                for (var i = 0; i < fg.Iterators.Count; i++)
                    fg.Iterators[i] = SplitExpr(fg.Iterators[i], toInsert, rng, usedNames);
                ProcessBlock(fg.Body, rng);
                break;
            case ReturnStmt rs:
                for (var i = 0; i < rs.Values.Count; i++)
                {
                    if (i == rs.Values.Count - 1 && rs.Values[i] is FunctionCallExpr) continue;
                    rs.Values[i] = SplitExpr(rs.Values[i], toInsert, rng, usedNames);
                }
                break;
            case FunctionDeclStmt fd:
                ProcessBlock(fd.FuncExpr.Body, rng);
                break;
        }
    }

    private static Expression SplitExpr(Expression expr, List<Statement> toInsert, Random rng, HashSet<string> usedNames)
    {
        if (expr is LiteralExpr or VarExpr or VarargsExpr)
            return expr;

        if (rng.NextDouble() < 0.3)
        {
            var tempName = UniqueName(usedNames, rng);
            toInsert.Add(new LocalVarStmt
            {
                Names = { tempName },
                Values = { expr }
            });
            return new VarExpr(tempName);
        }

        if (expr is BinaryExpr b)
        {
            var newLeft = SplitExpr(b.Left, toInsert, rng, usedNames);
            var newRight = SplitExpr(b.Right, toInsert, rng, usedNames);
            if (newLeft != b.Left || newRight != b.Right)
                return new BinaryExpr(b.Op, newLeft, newRight);
        }
        else if (expr is UnaryExpr u)
        {
            var newOp = SplitExpr(u.Operand, toInsert, rng, usedNames);
            if (newOp != u.Operand)
                return new UnaryExpr(u.Op, newOp);
        }

        return expr;
    }
}