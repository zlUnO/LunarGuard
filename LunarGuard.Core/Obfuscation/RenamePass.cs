using System.Security.Cryptography;
using System.Text;
using LunarGuard.Core.AST;
using LunarGuard.Core.AST.Stmt;
using LunarGuard.Core.AST.Expr;

namespace LunarGuard.Core.Obfuscation;

public class RenamePass : IObfuscationPass
{
    public string Name => "Rename Variables";

    private static readonly HashSet<string> Reserved = new()
    {
        "and", "break", "do", "else", "elseif", "end", "false", "for",
        "function", "if", "in", "local", "nil", "not", "or", "repeat",
        "return", "then", "true", "until", "while", "goto",
        "_VERSION", "_G", "self", "LoadLibrary", "assert", "collectgarbage",
        "dofile", "error", "getmetatable", "ipairs", "load", "loadfile",
        "next", "pairs", "pcall", "print", "rawequal", "rawget", "rawlen",
        "rawset", "select", "setmetatable", "tonumber", "tostring", "type",
        "unpack", "xpcall", "string", "table", "math", "io", "os", "debug",
        "coroutine", "utf8",
        "client", "entity", "ui", "renderer", "globals", "bit", "json",
        "database", "cvar", "config", "materialsystem", "panorama",
        "engine", "input", "surface", "vgui", "net", "filesystem",
        "keyvalues", "steam", "http", "chat", "console", "event",
        "entitylist", "globalvars", "inputsystem",
        "client_state", "engineclient", "enginevgui",
        "localize", "movedata", "physenv", "playerinfomanager",
        "prediction", "sound", "steamworks", "tier0", "vphysics",
        "gameeventmanager", "inputinternal",
        "cheat", "game", "menu", "notification", "log",
        "usercmd", "bsp", "trace", "concommand"
    };

    private readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();
    private readonly string _buildPrefix;

    public RenamePass()
    {
        // Unique per-instance (per-build) 32-char prefix
        var buf = new byte[16];
        _rng.GetBytes(buf);
        _buildPrefix = "_" + Convert.ToHexString(buf).ToLower();
    }

    public void Transform(BlockStmt root, ObfuscationOptions options)
    {
        if (!options.RenameVariables) return;

        var renames = new Dictionary<string, string>();
        var usedNames = new HashSet<string>(Reserved);
        var counter = 0L;
        var prefix = string.IsNullOrEmpty(options.RenamePrefix) ? _buildPrefix : options.RenamePrefix;

        string NextName()
        {
            while (true)
            {
                var name = $"{prefix}_{counter++}";
                if (usedNames.Add(name))
                    return name;
            }
        }

        var walker = new RenameWalker(renames, NextName);
        walker.VisitBlock(root);
    }

    private class RenameWalker
    {
        private readonly Dictionary<string, string> _renames;
        private readonly Func<string> _nextName;
        private readonly HashSet<string> _locals = new();
        private readonly Stack<HashSet<string>> _localScopes = new();

        public RenameWalker(Dictionary<string, string> renames, Func<string> nextName)
        {
            _renames = renames;
            _nextName = nextName;
        }

        public void VisitBlock(BlockStmt block)
        {
            _localScopes.Push(new HashSet<string>());
            VisitStmts(block.Statements);
            _localScopes.Pop();
        }

        private void VisitStmts(List<Statement> stmts)
        {
            foreach (var stmt in stmts)
                VisitStatement(stmt);
        }

        private void VisitStatement(Statement stmt)
        {
            switch (stmt)
            {
                case LocalVarStmt l:
                    for (var i = 0; i < l.Names.Count; i++)
                    {
                        var name = l.Names[i];
                        if (!_renames.ContainsKey(name))
                            _renames[name] = _nextName();
                        _localScopes.Peek().Add(name);
                        l.Names[i] = _renames[name];
                    }
                    foreach (var v in l.Values) VisitExpr(v);
                    break;

                case FunctionDeclStmt f:
                    if (f.IsLocal && f.Name != null)
                    {
                        if (!_renames.ContainsKey(f.Name))
                            _renames[f.Name] = _nextName();
                        _localScopes.Peek().Add(f.Name);
                        f.Name = _renames[f.Name];
                    }
                    else if (!f.IsMethod && f.Name != null && !char.IsUpper(f.Name[0]))
                    {
                        if (!_renames.ContainsKey(f.Name))
                            _renames[f.Name] = _nextName();
                        f.Name = _renames[f.Name];
                    }
                    if (f.NamePrefix != null && f.NamePrefix.Count > 0)
                    {
                        for (var i = 0; i < f.NamePrefix.Count; i++)
                        {
                            if (_renames.TryGetValue(f.NamePrefix[i], out var rn))
                                f.NamePrefix[i] = rn;
                        }
                    }
                    VisitFuncDecl(f.FuncExpr);
                    break;

                case AssignmentStmt a:
                    foreach (var t in a.Targets) VisitExpr(t);
                    foreach (var v in a.Values) VisitExpr(v);
                    break;

                case IfStmt i:
                    foreach (var (cond, body) in i.Branches)
                    { VisitExpr(cond); VisitBlock(body); }
                    if (i.ElseBody != null) VisitBlock(i.ElseBody);
                    break;

                case WhileStmt w:
                    VisitExpr(w.Condition); VisitBlock(w.Body);
                    break;

                case RepeatStmt r:
                    VisitBlock(r.Body); VisitExpr(r.Condition);
                    break;

                case ForNumericStmt fn:
                    if (!_renames.ContainsKey(fn.VarName))
                        _renames[fn.VarName] = _nextName();
                    _localScopes.Peek().Add(fn.VarName);
                    fn.VarName = _renames[fn.VarName];
                    VisitExpr(fn.Start); VisitExpr(fn.End);
                    if (fn.Step != null) VisitExpr(fn.Step);
                    VisitBlock(fn.Body);
                    break;

                case ForGenericStmt fg:
                    for (var i = 0; i < fg.VarNames.Count; i++)
                    {
                        var n = fg.VarNames[i];
                        if (!_renames.ContainsKey(n))
                            _renames[n] = _nextName();
                        _localScopes.Peek().Add(n);
                        fg.VarNames[i] = _renames[n];
                    }
                    foreach (var it in fg.Iterators) VisitExpr(it);
                    VisitBlock(fg.Body);
                    break;

                case FunctionCallStmt fc:
                    VisitExpr(fc.Call);
                    break;

                case DoStmt d:
                    VisitBlock(d.Body);
                    break;

                case ReturnStmt rs:
                    foreach (var v in rs.Values) VisitExpr(v);
                    break;
            }
        }

        private void VisitFuncDecl(FuncDeclExpr func)
        {
            _localScopes.Push(new HashSet<string>());
            for (var i = 0; i < func.Parameters.Count; i++)
            {
                var p = func.Parameters[i];
                if (!_renames.ContainsKey(p))
                    _renames[p] = _nextName();
                _localScopes.Peek().Add(p);
                func.Parameters[i] = _renames[p];
            }
            VisitBlock(func.Body);
            _localScopes.Pop();
        }

        private void VisitExpr(Expression expr)
        {
            switch (expr)
            {
                case VarExpr v:
                    if (_renames.TryGetValue(v.Name, out var renamed))
                        v.Name = renamed;
                    break;
                case BinaryExpr b:
                    VisitExpr(b.Left); VisitExpr(b.Right);
                    break;
                case UnaryExpr u:
                    VisitExpr(u.Operand);
                    break;
                case FunctionCallExpr fc:
                    VisitExpr(fc.Callee);
                    foreach (var a in fc.Arguments) VisitExpr(a);
                    break;
                case IndexExpr ix:
                    VisitExpr(ix.Object); VisitExpr(ix.Index);
                    break;
                case MemberExpr me:
                    VisitExpr(me.Object);
                    break;
                case TableConstructorExpr tc:
                    foreach (var f in tc.Fields)
                    { if (f.Key != null) VisitExpr(f.Key); VisitExpr(f.Value); }
                    break;
                case FuncDeclExpr fd:
                    VisitFuncDecl(fd);
                    break;
                case ConcatExpr cc:
                    foreach (var p in cc.Parts) VisitExpr(p);
                    break;
                case LiteralExpr: break;
                case VarargsExpr: break;
            }
        }
    }
}