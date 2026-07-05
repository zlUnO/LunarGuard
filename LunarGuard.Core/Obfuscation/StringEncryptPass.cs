using System.Text;
using LunarGuard.Core.AST;
using LunarGuard.Core.AST.Stmt;
using LunarGuard.Core.AST.Expr;

namespace LunarGuard.Core.Obfuscation;

public class StringEncryptPass : IObfuscationPass
{
    public string Name => "String Encryption";

    private readonly Random _rng = new();
    private int _nameCounter;

    public void Transform(BlockStmt root, ObfuscationOptions options)
    {
        if (!options.EncryptStrings) return;

        var stringMap = new Dictionary<string, string>();
        var key = options.StringKey;
        if (string.IsNullOrEmpty(key)) key = Keygen();

        CollectStrings(root, stringMap);
        var distinct = stringMap.Keys.Where(s => s.Length > 1).Distinct().ToList();
        if (distinct.Count == 0) return;

        var injectBefore = new List<Statement>();
        foreach (var s in distinct)
        {
            var varName = $"__s_{_nameCounter++}";
            stringMap[s] = varName;
            var enc = Encrypt(s, key);
            injectBefore.Add(MakeLocal(varName, enc, key));
        }

        root.Statements.InsertRange(0, injectBefore);
        ReplaceStrings(root, stringMap);
    }

    private string Keygen()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        return new string(Enumerable.Range(0, 8).Select(_ => chars[_rng.Next(chars.Length)]).ToArray());
    }

    private string Encrypt(string s, string key)
    {
        var result = new StringBuilder();
        for (var i = 0; i < s.Length; i++)
            result.Append((char)(s[i] - key[i % key.Length]));
        return result.ToString();
    }

    private Statement MakeLocal(string name, string encryptedValue, string key)
    {
        var loadStr = "local d,k=...;local s=''for i=1,#d do s=s..string.char(d[i]+k[(i-1)%#k+1])end return s";

        var encTable = new TableConstructorExpr();
        for (var i = 0; i < encryptedValue.Length; i++)
            encTable.Fields.Add(new TableField { Value = new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)(int)encryptedValue[i]) });

        var keyTable = new TableConstructorExpr();
        for (var i = 0; i < key.Length; i++)
            keyTable.Fields.Add(new TableField { Value = new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)(int)key[i]) });

        var innerCall = new FunctionCallExpr(new FunctionCallExpr(new VarExpr("load"))
        {
            Arguments = { new LiteralExpr(LiteralExpr.LiteralKind.String, loadStr) }
        })
        {
            Arguments = { encTable, keyTable }
        };

        return new LocalVarStmt
        {
            Names = { name },
            Values = { innerCall }
        };
    }

    private void CollectStrings(BlockStmt block, Dictionary<string, string> stringMap)
    {
        foreach (var stmt in block.Statements)
            CollectStringsStmt(stmt, stringMap);
    }

    private void CollectStringsStmt(Statement stmt, Dictionary<string, string> stringMap)
    {
        switch (stmt)
        {
            case LocalVarStmt l:
                foreach (var v in l.Values) CollectStringsExpr(v, stringMap);
                break;
            case AssignmentStmt a:
                foreach (var t in a.Targets) CollectStringsExpr(t, stringMap);
                foreach (var v in a.Values) CollectStringsExpr(v, stringMap);
                break;
            case IfStmt i:
                foreach (var (c, b) in i.Branches) { CollectStringsExpr(c, stringMap); CollectStrings(b, stringMap); }
                if (i.ElseBody != null) CollectStrings(i.ElseBody, stringMap);
                break;
            case WhileStmt w:
                CollectStringsExpr(w.Condition, stringMap); CollectStrings(w.Body, stringMap);
                break;
            case RepeatStmt r:
                CollectStrings(r.Body, stringMap); CollectStringsExpr(r.Condition, stringMap);
                break;
            case DoStmt d:
                CollectStrings(d.Body, stringMap);
                break;
            case ForNumericStmt fn:
                CollectStringsExpr(fn.Start, stringMap); CollectStringsExpr(fn.End, stringMap);
                if (fn.Step != null) CollectStringsExpr(fn.Step, stringMap);
                CollectStrings(fn.Body, stringMap);
                break;
            case ForGenericStmt fg:
                foreach (var it in fg.Iterators) CollectStringsExpr(it, stringMap);
                CollectStrings(fg.Body, stringMap);
                break;
            case FunctionCallStmt fc:
                CollectStringsExpr(fc.Call, stringMap);
                break;
            case ReturnStmt rs:
                foreach (var v in rs.Values) CollectStringsExpr(v, stringMap);
                break;
            case FunctionDeclStmt fd:
                CollectStrings(fd.FuncExpr.Body, stringMap);
                break;
        }
    }

    private void CollectStringsExpr(Expression expr, Dictionary<string, string> stringMap)
    {
        switch (expr)
        {
            case LiteralExpr l when l.Kind == LiteralExpr.LiteralKind.String && l.Value is string s:
                if (!stringMap.ContainsKey(s)) stringMap[s] = null!;
                break;
            case BinaryExpr b:
                CollectStringsExpr(b.Left, stringMap); CollectStringsExpr(b.Right, stringMap);
                break;
            case UnaryExpr u:
                CollectStringsExpr(u.Operand, stringMap);
                break;
            case FunctionCallExpr fc:
                CollectStringsExpr(fc.Callee, stringMap);
                foreach (var a in fc.Arguments) CollectStringsExpr(a, stringMap);
                break;
            case IndexExpr ix:
                CollectStringsExpr(ix.Object, stringMap); CollectStringsExpr(ix.Index, stringMap);
                break;
            case MemberExpr me:
                CollectStringsExpr(me.Object, stringMap);
                break;
            case TableConstructorExpr tc:
                foreach (var f in tc.Fields)
                { if (f.Key != null) CollectStringsExpr(f.Key, stringMap); CollectStringsExpr(f.Value, stringMap); }
                break;
            case FuncDeclExpr fd:
                CollectStrings(fd.Body, stringMap);
                break;
            case ConcatExpr cc:
                foreach (var p in cc.Parts) CollectStringsExpr(p, stringMap);
                break;
        }
    }

    private void ReplaceStrings(BlockStmt block, Dictionary<string, string> stringMap)
    {
        foreach (var stmt in block.Statements)
            ReplaceStringsStmt(stmt, stringMap);
    }

    private void ReplaceStringsStmt(Statement stmt, Dictionary<string, string> stringMap)
    {
        switch (stmt)
        {
            case LocalVarStmt l:
                for (var i = 0; i < l.Values.Count; i++)
                    l.Values[i] = ReplaceStringsExpr(l.Values[i], stringMap);
                break;
            case AssignmentStmt a:
                for (var i = 0; i < a.Targets.Count; i++)
                    a.Targets[i] = ReplaceStringsExpr(a.Targets[i], stringMap);
                for (var i = 0; i < a.Values.Count; i++)
                    a.Values[i] = ReplaceStringsExpr(a.Values[i], stringMap);
                break;
            case IfStmt i:
                for (var j = 0; j < i.Branches.Count; j++)
                {
                    var (c, b) = i.Branches[j];
                    i.Branches[j] = (ReplaceStringsExpr(c, stringMap), b);
                    ReplaceStrings(b, stringMap);
                }
                if (i.ElseBody != null) ReplaceStrings(i.ElseBody, stringMap);
                break;
            case WhileStmt w:
                w.Condition = ReplaceStringsExpr(w.Condition, stringMap);
                ReplaceStrings(w.Body, stringMap);
                break;
            case RepeatStmt r:
                ReplaceStrings(r.Body, stringMap);
                r.Condition = ReplaceStringsExpr(r.Condition, stringMap);
                break;
            case DoStmt d:
                ReplaceStrings(d.Body, stringMap);
                break;
            case ForNumericStmt fn:
                fn.Start = ReplaceStringsExpr(fn.Start, stringMap);
                fn.End = ReplaceStringsExpr(fn.End, stringMap);
                if (fn.Step != null) fn.Step = ReplaceStringsExpr(fn.Step, stringMap);
                ReplaceStrings(fn.Body, stringMap);
                break;
            case ForGenericStmt fg:
                for (var i = 0; i < fg.Iterators.Count; i++)
                    fg.Iterators[i] = ReplaceStringsExpr(fg.Iterators[i], stringMap);
                ReplaceStrings(fg.Body, stringMap);
                break;
            case FunctionCallStmt fc:
                fc.Call = (FunctionCallExpr)ReplaceStringsExpr(fc.Call, stringMap);
                break;
            case ReturnStmt rs:
                for (var i = 0; i < rs.Values.Count; i++)
                    rs.Values[i] = ReplaceStringsExpr(rs.Values[i], stringMap);
                break;
            case FunctionDeclStmt fd:
                ReplaceStrings(fd.FuncExpr.Body, stringMap);
                break;
        }
    }

    private Expression ReplaceStringsExpr(Expression expr, Dictionary<string, string> stringMap)
    {
        switch (expr)
        {
            case LiteralExpr l when l.Kind == LiteralExpr.LiteralKind.String && l.Value is string s && stringMap.TryGetValue(s, out var varName) && varName != null:
                return new VarExpr(varName);
            case BinaryExpr b:
                return new BinaryExpr(b.Op, ReplaceStringsExpr(b.Left, stringMap), ReplaceStringsExpr(b.Right, stringMap));
            case UnaryExpr u:
                return new UnaryExpr(u.Op, ReplaceStringsExpr(u.Operand, stringMap));
            case FunctionCallExpr fc:
                var fce = new FunctionCallExpr(ReplaceStringsExpr(fc.Callee, stringMap))
                {
                    IsMethodCall = fc.IsMethodCall, MethodName = fc.MethodName
                };
                foreach (var a in fc.Arguments) fce.Arguments.Add(ReplaceStringsExpr(a, stringMap));
                return fce;
            case IndexExpr ix:
                return new IndexExpr(ReplaceStringsExpr(ix.Object, stringMap), ReplaceStringsExpr(ix.Index, stringMap));
            case MemberExpr me:
                return new MemberExpr(ReplaceStringsExpr(me.Object, stringMap), me.Member);
            case TableConstructorExpr tc:
                var tce = new TableConstructorExpr();
                foreach (var f in tc.Fields)
                    tce.Fields.Add(new TableField
                    {
                        Key = f.Key != null ? ReplaceStringsExpr(f.Key, stringMap) : null,
                        Value = ReplaceStringsExpr(f.Value, stringMap)
                    });
                return tce;
            case FuncDeclExpr fd:
                ReplaceStrings(fd.Body, stringMap);
                return fd;
            case ConcatExpr cc:
                var cce = new ConcatExpr();
                foreach (var p in cc.Parts) cce.Parts.Add(ReplaceStringsExpr(p, stringMap));
                return cce;
        }
        return expr;
    }
}