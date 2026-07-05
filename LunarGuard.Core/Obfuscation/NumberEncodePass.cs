using LunarGuard.Core.AST;
using LunarGuard.Core.AST.Stmt;
using LunarGuard.Core.AST.Expr;

namespace LunarGuard.Core.Obfuscation;

public class NumberEncodePass : IObfuscationPass
{
    public string Name => "Number Encoding";

    public void Transform(BlockStmt root, ObfuscationOptions options)
    {
        if (!options.EncodeNumbers) return;

        var rng = new Random();
        ProcessBlock(root, rng);
    }

    private static void ProcessBlock(BlockStmt block, Random rng)
    {
        foreach (var stmt in block.Statements.ToList())
            ProcessStmt(stmt, rng);
    }

    private static void ProcessStmt(Statement stmt, Random rng)
    {
        switch (stmt)
        {
            case LocalVarStmt l:
                for (var i = 0; i < l.Values.Count; i++)
                    l.Values[i] = EncodeExpr(l.Values[i], rng);
                break;
            case AssignmentStmt a:
                for (var i = 0; i < a.Targets.Count; i++)
                    a.Targets[i] = EncodeExpr(a.Targets[i], rng);
                for (var i = 0; i < a.Values.Count; i++)
                    a.Values[i] = EncodeExpr(a.Values[i], rng);
                break;
            case IfStmt i:
                for (var j = 0; j < i.Branches.Count; j++)
                {
                    var (c, b) = i.Branches[j];
                    i.Branches[j] = (EncodeExpr(c, rng), b);
                    ProcessBlock(b, rng);
                }
                if (i.ElseBody != null) ProcessBlock(i.ElseBody, rng);
                break;
            case WhileStmt w:
                w.Condition = EncodeExpr(w.Condition, rng); ProcessBlock(w.Body, rng);
                break;
            case RepeatStmt r:
                ProcessBlock(r.Body, rng); r.Condition = EncodeExpr(r.Condition, rng);
                break;
            case DoStmt d:
                ProcessBlock(d.Body, rng);
                break;
            case ForNumericStmt fn:
                fn.Start = EncodeExpr(fn.Start, rng); fn.End = EncodeExpr(fn.End, rng);
                if (fn.Step != null) fn.Step = EncodeExpr(fn.Step, rng);
                ProcessBlock(fn.Body, rng);
                break;
            case ForGenericStmt fg:
                for (var i = 0; i < fg.Iterators.Count; i++)
                    fg.Iterators[i] = EncodeExpr(fg.Iterators[i], rng);
                ProcessBlock(fg.Body, rng);
                break;
            case FunctionCallStmt fc:
                fc.Call = (FunctionCallExpr)EncodeExpr(fc.Call, rng);
                break;
            case ReturnStmt rs:
                for (var i = 0; i < rs.Values.Count; i++)
                    rs.Values[i] = EncodeExpr(rs.Values[i], rng);
                break;
            case FunctionDeclStmt fd:
                ProcessBlock(fd.FuncExpr.Body, rng);
                break;
        }
    }

    private static Expression EncodeExpr(Expression expr, Random rng)
    {
        switch (expr)
        {
            case LiteralExpr l when l.Kind == LiteralExpr.LiteralKind.Number && l.Value is not null:
                var numVal = l.Value is long lng ? (double)lng : (double)l.Value;
                if ((numVal == (long)numVal || Math.Abs(numVal) is >= 0.001 and < 1000000) && numVal != 0)
                    return EncodeNumber(numVal, rng);
                break;
            case BinaryExpr b:
                return new BinaryExpr(b.Op, EncodeExpr(b.Left, rng), EncodeExpr(b.Right, rng));
            case UnaryExpr u:
                return new UnaryExpr(u.Op, EncodeExpr(u.Operand, rng));
            case FunctionCallExpr fc:
                var fce = new FunctionCallExpr(EncodeExpr(fc.Callee, rng))
                {
                    IsMethodCall = fc.IsMethodCall,
                    MethodName = fc.MethodName
                };
                foreach (var a in fc.Arguments)
                    fce.Arguments.Add(EncodeExpr(a, rng));
                return fce;
            case IndexExpr ix:
                return new IndexExpr(EncodeExpr(ix.Object, rng), EncodeExpr(ix.Index, rng));
            case MemberExpr me:
                return new MemberExpr(EncodeExpr(me.Object, rng), me.Member);
            case TableConstructorExpr tc:
                var tce = new TableConstructorExpr();
                foreach (var f in tc.Fields)
                    tce.Fields.Add(new TableField
                    {
                        Key = f.Key != null ? EncodeExpr(f.Key, rng) : null,
                        Value = EncodeExpr(f.Value, rng)
                    });
                return tce;
            case FuncDeclExpr fd:
                ProcessBlock(fd.Body, rng);
                return fd;
            case ConcatExpr cc:
                var cce = new ConcatExpr();
                foreach (var p in cc.Parts)
                    cce.Parts.Add(EncodeExpr(p, rng));
                return cce;
        }
        return expr;
    }

    private static Expression EncodeNumber(double num, Random rng)
    {
        var pattern = rng.Next(4);
        return pattern switch
        {
            0 => EncodeAddSub(num, rng),
            1 => EncodeMulDiv(num, rng),
            2 => EncodeAddSub(num, rng),
            _ => EncodeNested(num, rng),
        };
    }

    private static Expression EncodeAddSub(double num, Random rng)
    {
        var a = num > 0
            ? rng.Next(1, Math.Max(2, (int)Math.Ceiling(Math.Min(num, 10000))))
            : rng.Next(1, Math.Max(2, (int)Math.Ceiling(Math.Min(-num, 10000))));
        var b = num - a;
        if (b >= 0)
            return new BinaryExpr(BinaryOp.Add, MakeLiteral(a), MakeLiteral(b));
        return new BinaryExpr(BinaryOp.Subtract, MakeLiteral(a), MakeLiteral(-b));
    }

    private static Expression EncodeMulDiv(double num, Random rng)
    {
        if (num <= 0 || num > 100000) return EncodeAddSub(num, rng);
        var divisor = rng.Next(2, Math.Max(3, (int)Math.Min(num, 100)));
        var quotient = num / divisor;
        if (Math.Abs(quotient - Math.Round(quotient)) < 0.001)
        {
            var q = (long)Math.Round(quotient);
            if (q is > 0 and < 100000)
                return new BinaryExpr(BinaryOp.Multiply,
                    MakeLiteral(divisor),
                    MakeLiteral(q));
        }
        return EncodeAddSub(num, rng);
    }

    private static Expression EncodeNested(double num, Random rng)
    {
        var mid = rng.Next(1, Math.Max(2, (int)Math.Ceiling(Math.Min(Math.Abs(num), 1000))));
        var inner = EncodeAddSub(mid > num ? mid + Math.Abs(num) : Math.Abs(num) - mid, rng);
        if (num >= 0)
            return new BinaryExpr(BinaryOp.Add, inner, MakeLiteral(num - mid));
        return new BinaryExpr(BinaryOp.Subtract, MakeLiteral(0), inner);
    }

    private static LiteralExpr MakeLiteral(double val)
    {
        if (val == (long)val)
            return new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)val);
        return new LiteralExpr(LiteralExpr.LiteralKind.Number, val);
    }
}