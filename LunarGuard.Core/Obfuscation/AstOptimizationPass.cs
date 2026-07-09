using LunarGuard.Core.AST;
using LunarGuard.Core.AST.Stmt;
using LunarGuard.Core.AST.Expr;

namespace LunarGuard.Core.Obfuscation;

public class AstOptimizationPass : IObfuscationPass
{
    public string Name => "AST Optimization";

    public void Transform(BlockStmt root, ObfuscationOptions options)
    {
        if (!options.OptimizeAst) return;
        ConstantFoldBlock(root);
        DeadCodeEliminateBlock(root);
    }

    private static Expression? ConstantFold(Expression expr)
    {
        if (expr is BinaryExpr b)
        {
            var lc = ConstantFold(b.Left);
            var rc = ConstantFold(b.Right);
            if (lc is LiteralExpr ll && rc is LiteralExpr rr)
            {
                if (ll.Kind == LiteralExpr.LiteralKind.Number && rr.Kind == LiteralExpr.LiteralKind.Number)
                {
                    var lv = (double)(long)ll.Value!;
                    var rv = (double)(long)rr.Value!;
                    double result = b.Op switch
                    {
                        BinaryOp.Add => lv + rv,
                        BinaryOp.Subtract => lv - rv,
                        BinaryOp.Multiply => lv * rv,
                        BinaryOp.Divide => rv != 0 ? lv / rv : double.NaN,
                        BinaryOp.Modulo => rv != 0 ? lv % rv : double.NaN,
                        BinaryOp.Power => Math.Pow(lv, rv),
                        _ => double.NaN
                    };
                    if (!double.IsNaN(result) && !double.IsInfinity(result))
                    {
                        if (result == Math.Floor(result) && result <= long.MaxValue && result >= long.MinValue)
                            return new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)result);
                        return new LiteralExpr(LiteralExpr.LiteralKind.Number, result);
                    }
                }
                if (b.Op == BinaryOp.Concat && ll.Kind == LiteralExpr.LiteralKind.String && rr.Kind == LiteralExpr.LiteralKind.String)
                {
                    return new LiteralExpr(LiteralExpr.LiteralKind.String, (string)ll.Value! + (string)rr.Value!);
                }
            }
            return b;
        }
        if (expr is UnaryExpr u)
        {
            var oc = ConstantFold(u.Operand);
            if (oc is LiteralExpr ol)
            {
                if (u.Op == UnaryOp.Negate && ol.Kind == LiteralExpr.LiteralKind.Number)
                {
                    var v = (long)ol.Value!;
                    return new LiteralExpr(LiteralExpr.LiteralKind.Number, -v);
                }
                if (u.Op == UnaryOp.Not && ol.Kind == LiteralExpr.LiteralKind.Boolean)
                {
                    return new LiteralExpr(LiteralExpr.LiteralKind.Boolean, !(bool)ol.Value!);
                }
                if (u.Op == UnaryOp.Length && ol.Kind == LiteralExpr.LiteralKind.String)
                {
                    return new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)((string)ol.Value!).Length);
                }
            }
            return u;
        }
        return expr;
    }

    private static void ConstantFoldBlock(BlockStmt block)
    {
        foreach (var stmt in block.Statements.ToList())
        {
            switch (stmt)
            {
                case AssignmentStmt a:
                    for (var i = 0; i < a.Values.Count; i++)
                        a.Values[i] = ConstantFold(a.Values[i])!;
                    break;
                case LocalVarStmt l:
                    for (var i = 0; i < l.Values.Count; i++)
                        l.Values[i] = ConstantFold(l.Values[i])!;
                    break;
                case IfStmt ifStmt:
                    for (var j = 0; j < ifStmt.Branches.Count; j++)
                    {
                        var branch = ifStmt.Branches[j];
                        var folded = ConstantFold(branch.Condition)!;
                        ifStmt.Branches[j] = (folded, branch.Body);
                        ConstantFoldBlock(branch.Body);
                    }
                    if (ifStmt.ElseBody != null)
                        ConstantFoldBlock(ifStmt.ElseBody);
                    break;
                case WhileStmt w:
                    w.Condition = ConstantFold(w.Condition)!;
                    ConstantFoldBlock(w.Body);
                    break;
                case RepeatStmt r:
                    r.Condition = ConstantFold(r.Condition)!;
                    ConstantFoldBlock(r.Body);
                    break;
                case ForNumericStmt fn:
                    fn.Start = ConstantFold(fn.Start)!;
                    fn.End = ConstantFold(fn.End)!;
                    if (fn.Step != null) fn.Step = ConstantFold(fn.Step)!;
                    ConstantFoldBlock(fn.Body);
                    break;
                case ForGenericStmt fg:
                    for (var i = 0; i < fg.Iterators.Count; i++)
                        fg.Iterators[i] = ConstantFold(fg.Iterators[i])!;
                    ConstantFoldBlock(fg.Body);
                    break;
                case DoStmt d:
                    ConstantFoldBlock(d.Body);
                    break;
                case FunctionDeclStmt fd:
                    ConstantFoldBlock(fd.FuncExpr.Body);
                    break;
                case ReturnStmt rs:
                    for (var i = 0; i < rs.Values.Count; i++)
                        rs.Values[i] = ConstantFold(rs.Values[i])!;
                    break;
            }
        }
    }

    private static void DeadCodeEliminateBlock(BlockStmt block)
    {
        block.Statements.RemoveAll(s =>
        {
            if (s is IfStmt ifStmt && ifStmt.Branches.Count == 1 && ifStmt.ElseBody == null)
            {
                var (cond, body) = ifStmt.Branches[0];
                if (cond is LiteralExpr l && l.Kind == LiteralExpr.LiteralKind.Boolean)
                {
                    if ((bool)l.Value!)
                    {
                        block.Statements.InsertRange(block.Statements.IndexOf(s), body.Statements);
                        return true;
                    }
                    return true;
                }
            }
            return false;
        });

        foreach (var stmt in block.Statements)
        {
            switch (stmt)
            {
                case IfStmt ifStmt:
                    foreach (var (_, body) in ifStmt.Branches)
                        DeadCodeEliminateBlock(body);
                    if (ifStmt.ElseBody != null)
                        DeadCodeEliminateBlock(ifStmt.ElseBody);
                    break;
                case WhileStmt w:
                    DeadCodeEliminateBlock(w.Body);
                    break;
                case RepeatStmt r:
                    DeadCodeEliminateBlock(r.Body);
                    break;
                case ForNumericStmt fn:
                    DeadCodeEliminateBlock(fn.Body);
                    break;
                case ForGenericStmt fg:
                    DeadCodeEliminateBlock(fg.Body);
                    break;
                case DoStmt d:
                    DeadCodeEliminateBlock(d.Body);
                    break;
                case FunctionDeclStmt fd:
                    DeadCodeEliminateBlock(fd.FuncExpr.Body);
                    break;
            }
        }
    }
}
