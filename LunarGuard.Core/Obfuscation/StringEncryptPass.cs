using System.Security.Cryptography;
using System.Text;
using LunarGuard.Core.AST;
using LunarGuard.Core.AST.Stmt;
using LunarGuard.Core.AST.Expr;

namespace LunarGuard.Core.Obfuscation;

public class StringEncryptPass : IObfuscationPass
{
    public string Name => "String Encryption";

    private readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();
    private int _nameCounter;
    private int _funcCounter;
    private readonly List<Statement> _scatteredDecoders = new();

    public void Transform(BlockStmt root, ObfuscationOptions options)
    {
        if (!options.EncryptStrings) return;

        var stringMap = new Dictionary<string, string>();
        CollectStrings(root, stringMap);
        var distinct = stringMap.Keys.Where(s => s.Length > 1).Distinct().ToList();
        if (distinct.Count == 0) return;

        _scatteredDecoders.Clear();
        _nameCounter = 0;
        _funcCounter = 0;

        foreach (var s in distinct)
        {
            var varName = $"__se_{_nameCounter++}";
            stringMap[s] = varName;
            var decoder = MakeScatteredDecoder(s, varName);
            _scatteredDecoders.Add(decoder);
        }

        // Scatter decoders throughout the root block (not all at top)
        ScatterDecoders(root);

        ReplaceStrings(root, stringMap);
    }

    private void ScatterDecoders(BlockStmt root)
    {
        var insertPoints = new List<int>();
        for (var i = 0; i < root.Statements.Count; i++)
        {
            var s = root.Statements[i];
            if (s is FunctionDeclStmt) continue;
            if (s is LocalVarStmt lv && lv.Names.Count == 1 && lv.Names[0].Contains("_run")) continue;
            insertPoints.Add(i);
        }

        if (insertPoints.Count == 0) { insertPoints.Add(0); }

        for (var i = 0; i < _scatteredDecoders.Count; i++)
        {
            var posIdx = i % insertPoints.Count;
            var pos = insertPoints[posIdx];
            root.Statements.Insert(pos, _scatteredDecoders[i]);
            for (var j = posIdx; j < insertPoints.Count; j++)
                insertPoints[j]++;
        }
    }

    private Statement MakeScatteredDecoder(string original, string varName)
    {
        // Each string gets its OWN unique decoder with varying algorithm
        var buf = new byte[1];
        _rng.GetBytes(buf);
        var algo = buf[0] % 4;

        return algo switch
        {
            0 => MakeXorDecoder(original, varName),
            1 => MakeOffsetDecoder(original, varName),
            2 => MakeTableDecoder(original, varName),
            _ => MakeArithDecoder(original, varName),
        };
    }

    private Statement MakeXorDecoder(string original, string varName)
    {
        var key = new byte[8];
        _rng.GetBytes(key);
        var keyNum = BitConverter.ToInt32(key, 0) & 0x7FFFFFFF;

        var encoded = new List<long>();
        foreach (var c in original)
            encoded.Add(c ^ (keyNum & 0xFF));

        return BuildDecoderStmt(varName, encoded, keyNum);
    }

    private Statement MakeOffsetDecoder(string original, string varName)
    {
        var key = new byte[2];
        _rng.GetBytes(key);

        var encoded = new List<long>();
        foreach (var c in original)
            encoded.Add(c - key[0]);

        return BuildDecoderStmt(varName, encoded, (long)key[0]);
    }

    private Statement MakeTableDecoder(string original, string varName)
    {
        var key = new byte[4];
        _rng.GetBytes(key);
        var baseKey = BitConverter.ToInt32(key, 0) & 0x7FFFFFFF;

        var encoded = new List<long>();
        for (var i = 0; i < original.Length; i++)
            encoded.Add(original[i] ^ ((baseKey + i * 7) & 0xFF));

        return BuildDecoderStmt(varName, encoded, baseKey);
    }

    private Statement MakeArithDecoder(string original, string varName)
    {
        var key = new byte[4];
        _rng.GetBytes(key);
        var keyNum = Math.Max(1, (BitConverter.ToInt32(key, 0) & 0x7FFFFFFF) % 1000 + 1);

        var encoded = new List<long>();
        foreach (var c in original)
            encoded.Add(c * keyNum);

        var p = _funcCounter++;
        var funcBody = $"local t=...;local r=''for i=1,#t do r=r..string.char(t[i]/{keyNum})end return r";
        var loadStr = $"local t=...;local r=''for i=1,#t do r=r..string.char(t[i]/{keyNum})end return r";

        var encTable = new TableConstructorExpr();
        foreach (var v in encoded)
            encTable.Fields.Add(new TableField
            {
                Value = new LiteralExpr(LiteralExpr.LiteralKind.Number, v)
            });

        var decoderCall = new FunctionCallExpr(
            new FunctionCallExpr(new VarExpr("load"))
            {
                Arguments =
                {
                    new LiteralExpr(LiteralExpr.LiteralKind.String, loadStr),
                    new LiteralExpr(LiteralExpr.LiteralKind.String, $"=dec_{p}")
                }
            })
        {
            Arguments = { encTable }
        };

        return new LocalVarStmt
        {
            Names = { varName },
            Values = { decoderCall }
        };
    }

    private Statement BuildDecoderStmt(string varName, List<long> encoded, long key)
    {
        var p = _funcCounter++;

        // Generate decoder with per-string unique algorithm
        var op = (p % 4) switch
        {
            0 => "+",
            1 => "-",
            2 => "~", // XOR in Lua 5.3+
            _ => "+"
        };

        var negKey = op == "-" ? key : -key;

        // Decoder as a load string (unique per string)
        var decoderStr = op switch
        {
            "+" => $"local d=...;local r=''for i=1,#d do r=r..string.char(d[i]-{key})end return r",
            "-" => $"local d=...;local r=''for i=1,#d do r=r..string.char(d[i]+{key})end return r",
            "~" => $"local d=...;local r=''for i=1,#d do r=r..string.char(d[i]~{key})end return r",
            _ => $"local d=...;local r=''for i=1,#d do r=r..string.char(d[i]-{key})end return r",
        };

        var encTable = new TableConstructorExpr();
        foreach (var v in encoded)
            encTable.Fields.Add(new TableField
            {
                Value = new LiteralExpr(LiteralExpr.LiteralKind.Number, v)
            });

        var decoderCall = new FunctionCallExpr(
            new FunctionCallExpr(new VarExpr("load"))
            {
                Arguments =
                {
                    new LiteralExpr(LiteralExpr.LiteralKind.String, decoderStr),
                    new LiteralExpr(LiteralExpr.LiteralKind.String, $"=dec_{p}")
                }
            })
        {
            Arguments = { encTable }
        };

        return new LocalVarStmt
        {
            Names = { varName },
            Values = { decoderCall }
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