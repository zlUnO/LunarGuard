using System.Security.Cryptography;
using System.Text;
using LunarGuard.Core.AST;
using LunarGuard.Core.AST.Stmt;
using LunarGuard.Core.AST.Expr;

namespace LunarGuard.Core.Obfuscation.Virtualization;

public class VirtualizationPass : IObfuscationPass
{
    public string Name => "Bytecode Virtualization";

    private static int _sharedVmId = 0;

    public void Transform(BlockStmt root, ObfuscationOptions options)
    {
        if (!options.Virtualize) return;

        var statements = root.Statements.ToList();
        var virtualized = new List<(Statement Stmt, Statement Original)>();
        var anyFunctionFound = false;

        foreach (var stmt in statements)
        {
            if (stmt is FunctionDeclStmt fd)
            {
                // Skip functions that capture upvalues (outer-scope locals):
                // the VM has a flat register model with no closure support, so a
                // captured local would be resolved via GETG and become nil at runtime.
                if (CapturesUpvalue(fd.FuncExpr))
                    continue;

                anyFunctionFound = true;

                // Lower complex constructs BEFORE VM compilation
                LowerComplexConstructs(fd.FuncExpr.Body);

                var rng = RandomNumberGenerator.Create();
                var vmPrefix = $"vm_{NextHex(rng, 8)}";
                var xorKey = NextHex(rng, 4);
                var vmm = new VmGenerator(vmPrefix, rng, xorKey);
                var virt = vmm.VirtualizeFunction(fd);
                virtualized.Add((virt, stmt));
            }
        }

        if (!anyFunctionFound) return;

        // Replace original functions with virtualized versions
        foreach (var (vStmt, original) in virtualized)
        {
            var idx = root.Statements.IndexOf(original);
            root.Statements.Insert(idx, vStmt);
            root.Statements.Remove(original);
        }

        // Insert a single shared VM interpreter at the start of the root
        var sharedRng = RandomNumberGenerator.Create();
        var sharedVm = CreateSharedVM(sharedRng);
        root.Statements.Insert(0, sharedVm);
    }

    /// <summary>
    /// Lower complex control flow constructs to simpler ones the VM can handle.
    /// - ForGenericStmt → While + iterator protocol
    /// - RepeatStmt → While + break
    /// </summary>
    private static void LowerComplexConstructs(BlockStmt block)
    {
        var lowered = new List<Statement>();
        foreach (var stmt in block.Statements)
        {
            switch (stmt)
            {
                case ForGenericStmt fg:
                    lowered.Add(LowerForGeneric(fg));
                    break;
                case RepeatStmt rs:
                    lowered.Add(LowerRepeat(rs));
                    break;
                case IfStmt i:
                    for (var j = 0; j < i.Branches.Count; j++)
                    {
                        var (cond, body) = i.Branches[j];
                        LowerComplexConstructs(body);
                    }
                    if (i.ElseBody != null)
                        LowerComplexConstructs(i.ElseBody);
                    lowered.Add(stmt);
                    break;
                case WhileStmt w:
                    LowerComplexConstructs(w.Body);
                    lowered.Add(stmt);
                    break;
                case DoStmt d:
                    LowerComplexConstructs(d.Body);
                    lowered.Add(stmt);
                    break;
                case ForNumericStmt fn:
                    LowerComplexConstructs(fn.Body);
                    lowered.Add(stmt);
                    break;
                case FunctionDeclStmt fd:
                    LowerComplexConstructs(fd.FuncExpr.Body);
                    lowered.Add(stmt);
                    break;
                default:
                    lowered.Add(stmt);
                    break;
            }
        }
        block.Statements.Clear();
        block.Statements.AddRange(lowered);
    }

    // Names that are safe to treat as globals (resolved via GETG at runtime).
    private static readonly HashSet<string> GlobalLike = new()
    {
        "_G", "_VERSION", "self", "unpack", "pairs", "ipairs", "type", "tostring",
        "tonumber", "assert", "error", "pcall", "xpcall", "select", "rawequal",
        "rawget", "rawset", "getmetatable", "setmetatable", "load", "loadfile",
        "dofile", "collectgarbage", "next", "print", "string", "table", "math",
        "io", "os", "debug", "coroutine", "utf8", "client", "entity", "ui",
        "renderer", "globals", "bit", "json", "database", "cvar", "config",
        "materialsystem", "panorama", "engine", "input", "surface", "vgui",
        "filesystem", "http", "lz4", "enums", "cheat", "eventlog",
        "playertags", "weapondata", "aimbot", "triggerbot", "esp", "misc",
        "chams", "indicators", "hitmarkers", "world", "logs", "notifications",
        "paint", "screen", "fonts", "textures", "usercmd", "trace",
    };

    /// <summary>
    /// Detects whether a function (or any nested function within it) references a
    /// variable that is not declared in its own scope and is not a known global.
    /// Such a reference is an upvalue (closure capture) which the flat VM register
    /// model cannot represent, so the function must not be virtualized.
    /// </summary>
    private static bool CapturesUpvalue(FuncDeclExpr func)
    {
        return CapturesUpvalue(func, new HashSet<string>());
    }

    private static bool CapturesUpvalue(FuncDeclExpr func, HashSet<string> enclosing)
    {
        var scope = new HashSet<string>(enclosing);
        foreach (var p in func.Parameters) scope.Add(p);
        CollectScopeDecls(func.Body, scope);

        var refs = new HashSet<string>();
        var nested = new List<FuncDeclExpr>();
        CollectScopeRefs(func.Body, refs, nested);

        foreach (var r in refs)
        {
            if (scope.Contains(r) || GlobalLike.Contains(r)) continue;
            return true;
        }
        foreach (var nf in nested)
            if (CapturesUpvalue(nf, scope)) return true;
        return false;
    }

    private static void CollectScopeDecls(BlockStmt block, HashSet<string> scope)
    {
        foreach (var stmt in block.Statements)
        {
            switch (stmt)
            {
                case LocalVarStmt l:
                    foreach (var n in l.Names) scope.Add(n);
                    // function literals in values open a new scope; skip their bodies
                    break;
                case ForNumericStmt fn:
                    scope.Add(fn.VarName);
                    CollectScopeDecls(fn.Body, scope);
                    break;
                case ForGenericStmt fg:
                    foreach (var n in fg.VarNames) scope.Add(n);
                    CollectScopeDecls(fg.Body, scope);
                    break;
                case IfStmt i:
                    foreach (var (_, b) in i.Branches) CollectScopeDecls(b, scope);
                    if (i.ElseBody != null) CollectScopeDecls(i.ElseBody, scope);
                    break;
                case WhileStmt w:
                    CollectScopeDecls(w.Body, scope);
                    break;
                case RepeatStmt r:
                    CollectScopeDecls(r.Body, scope);
                    break;
                case DoStmt d:
                    CollectScopeDecls(d.Body, scope);
                    break;
                case FunctionDeclStmt f when f.Name != null:
                    scope.Add(f.Name);
                    // body is a separate scope; do not recurse here
                    break;
            }
        }
    }

    private static void CollectScopeRefs(BlockStmt block, HashSet<string> refs, List<FuncDeclExpr> nested)
    {
        foreach (var stmt in block.Statements)
        {
            switch (stmt)
            {
                case LocalVarStmt l:
                    foreach (var v in l.Values) CollectExprRefs(v, refs, nested);
                    break;
                case AssignmentStmt a:
                    foreach (var t in a.Targets) CollectExprRefs(t, refs, nested);
                    foreach (var v in a.Values) CollectExprRefs(v, refs, nested);
                    break;
                case IfStmt i:
                    foreach (var (c, b) in i.Branches) { CollectExprRefs(c, refs, nested); CollectScopeRefs(b, refs, nested); }
                    if (i.ElseBody != null) CollectScopeRefs(i.ElseBody, refs, nested);
                    break;
                case WhileStmt w:
                    CollectExprRefs(w.Condition, refs, nested);
                    CollectScopeRefs(w.Body, refs, nested);
                    break;
                case RepeatStmt r:
                    CollectScopeRefs(r.Body, refs, nested);
                    CollectExprRefs(r.Condition, refs, nested);
                    break;
                case DoStmt d:
                    CollectScopeRefs(d.Body, refs, nested);
                    break;
                case ForNumericStmt fn:
                    CollectExprRefs(fn.Start, refs, nested);
                    CollectExprRefs(fn.End, refs, nested);
                    if (fn.Step != null) CollectExprRefs(fn.Step, refs, nested);
                    CollectScopeRefs(fn.Body, refs, nested);
                    break;
                case ForGenericStmt fg:
                    foreach (var it in fg.Iterators) CollectExprRefs(it, refs, nested);
                    CollectScopeRefs(fg.Body, refs, nested);
                    break;
                case FunctionCallStmt fc:
                    CollectExprRefs(fc.Call, refs, nested);
                    break;
                case ReturnStmt rs:
                    foreach (var v in rs.Values) CollectExprRefs(v, refs, nested);
                    break;
                case FunctionDeclStmt f:
                    // only the body opens a new scope; the name/args are handled by recursion
                    CollectScopeRefs(f.FuncExpr.Body, refs, nested);
                    break;
            }
        }
    }

    private static void CollectExprRefs(Expression expr, HashSet<string> refs, List<FuncDeclExpr> nested)
    {
        switch (expr)
        {
            case VarExpr v:
                refs.Add(v.Name);
                break;
            case BinaryExpr b:
                CollectExprRefs(b.Left, refs, nested);
                CollectExprRefs(b.Right, refs, nested);
                break;
            case UnaryExpr u:
                CollectExprRefs(u.Operand, refs, nested);
                break;
            case FunctionCallExpr fc:
                CollectExprRefs(fc.Callee, refs, nested);
                foreach (var a in fc.Arguments) CollectExprRefs(a, refs, nested);
                break;
            case IndexExpr ix:
                CollectExprRefs(ix.Object, refs, nested);
                CollectExprRefs(ix.Index, refs, nested);
                break;
            case MemberExpr me:
                CollectExprRefs(me.Object, refs, nested);
                break;
            case TableConstructorExpr tc:
                foreach (var f in tc.Fields)
                { if (f.Key != null) CollectExprRefs(f.Key, refs, nested); CollectExprRefs(f.Value, refs, nested); }
                break;
            case FuncDeclExpr fd:
                nested.Add(fd);
                // arguments/body handled by recursion over nested list
                break;
            case ConcatExpr cc:
                foreach (var p in cc.Parts) CollectExprRefs(p, refs, nested);
                break;
            case LiteralExpr:
            case VarargsExpr:
                break;
        }
    }

    private static string UniqueName(ISet<string> used, string prefix)
    {
        var i = 0;
        while (true)
        {
            var name = $"{prefix}_{i}";
            if (used.Add(name)) return name;
            i++;
        }
    }

    /// <summary>
    /// for k,v in pairs(t) do body end
    /// →
    /// do local f,s,var = pairs(t); while true do local r1,r2 = f(s,var); var = r1; if var == nil then break end; k = r1; v = r2; body end end
    /// </summary>
    private static Statement LowerForGeneric(ForGenericStmt fg)
    {
        var used = new HashSet<string>();
        foreach (var vn in fg.VarNames) used.Add(vn);
        var fName = UniqueName(used, "_f");
        var sName = UniqueName(used, "_s");
        var varName = UniqueName(used, "_var");
        var r1Name = UniqueName(used, "_r1");
        var r2Name = UniqueName(used, "_r2");

        var outerBlock = new BlockStmt();

        // local f,s,var = <iterators>
        var iterCall = new FunctionCallExpr(fg.Iterators[0]);
        for (var i = 1; i < fg.Iterators.Count; i++)
            iterCall.Arguments.Add(fg.Iterators[i]);

        outerBlock.Statements.Add(new LocalVarStmt
        {
            Names = { fName, sName, varName },
            Values = { iterCall }
        });

        // while true do
        var innerBlock = new BlockStmt();

        // local r1, r2 = f(s, var)
        innerBlock.Statements.Add(new LocalVarStmt
        {
            Names = { r1Name, r2Name },
            Values =
            {
                new FunctionCallExpr(new VarExpr(fName))
                {
                    Arguments = { new VarExpr(sName), new VarExpr(varName) }
                }
            }
        });

        // var = r1
        innerBlock.Statements.Add(new AssignmentStmt
        {
            Targets = { new VarExpr(varName) },
            Values = { new VarExpr(r1Name) }
        });

        // if var == nil then break end
        innerBlock.Statements.Add(new IfStmt
        {
            Branches =
            {
                (
                    new BinaryExpr(BinaryOp.Eq, new VarExpr(varName),
                        new LiteralExpr(LiteralExpr.LiteralKind.Nil, null)),
                    new BlockStmt
                    {
                        Statements = { new BreakStmt() }
                    }
                )
            }
        });

        // k = r1; v = r2 (for each loop var)
        var assignmentBlock = new BlockStmt();
        for (var i = 0; i < fg.VarNames.Count; i++)
        {
            var src = i == 0 ? r1Name : r2Name;
            assignmentBlock.Statements.Add(new AssignmentStmt
            {
                Targets = { new VarExpr(fg.VarNames[i]) },
                Values = { new VarExpr(src) }
            });
        }
        innerBlock.Statements.AddRange(assignmentBlock.Statements);

        // body
        innerBlock.Statements.AddRange(fg.Body.Statements);
        fg.Body.Statements.Clear();

        var whileStmt = new WhileStmt
        {
            Condition = new LiteralExpr(LiteralExpr.LiteralKind.Boolean, true),
            Body = innerBlock
        };

        outerBlock.Statements.Add(whileStmt);

        // Wrap in a do-end block to isolate locals
        var doStmt = new DoStmt { Body = outerBlock };
        return doStmt;
    }

    /// <summary>
    /// repeat body until cond → while true do body; if cond then break end end
    /// </summary>
    private static Statement LowerRepeat(RepeatStmt rs)
    {
        var innerBlock = new BlockStmt();
        innerBlock.Statements.AddRange(rs.Body.Statements);
        rs.Body.Statements.Clear();

        innerBlock.Statements.Add(new IfStmt
        {
            Branches =
            {
                (
                    rs.Condition,
                    new BlockStmt
                    {
                        Statements = { new BreakStmt() }
                    }
                )
            }
        });

        return new WhileStmt
        {
            Condition = new LiteralExpr(LiteralExpr.LiteralKind.Boolean, true),
            Body = innerBlock
        };
    }

    private static Statement CreateSharedVM(RandomNumberGenerator rng)
    {
        _sharedVmId++;
        var id = _sharedVmId;
        var prefix = $"__lgsvm{id}";

        // Handlers defined inside __lgsvm1_run so they close over code/stk/regs/pc/sp as upvalues
        // Keep the guard so this only runs once; use local handlers to avoid global pollution
        var vmSrc = $@"
if not {prefix}_run then
{prefix}_stk = {{}}; {prefix}_regs = {{}}; {prefix}_sp = 0
function {prefix}_push(v) {prefix}_sp = {prefix}_sp + 1; {prefix}_stk[{prefix}_sp] = v end
function {prefix}_pop() local v = {prefix}_stk[{prefix}_sp]; {prefix}_stk[{prefix}_sp] = nil; {prefix}_sp = {prefix}_sp - 1; return v end
function {prefix}_run(code, key)
    local stk, regs, pc, sp = {prefix}_stk, {prefix}_regs, 1, 0
    for i = 1, #code do
        local v = code[i]
        if type(v) == 'number' and v == math.floor(v) then
            code[i] = v + key
        end
    end
    local h = {{}}
    h[0] = function() end
    h[1] = function() sp = sp + 1; stk[sp] = code[pc]; pc = pc + 1 end
    h[2] = function() sp = sp - 1; stk[sp + 1] = nil end
    h[3] = function() local a = {prefix}_pop(); local b = {prefix}_pop(); {prefix}_push(a + b) end
    h[4] = function() local a = {prefix}_pop(); local b = {prefix}_pop(); {prefix}_push(b - a) end
    h[5] = function() local a = {prefix}_pop(); local b = {prefix}_pop(); {prefix}_push(a * b) end
    h[6] = function() local a = {prefix}_pop(); local b = {prefix}_pop(); {prefix}_push(b / a) end
    h[7] = function() local a = {prefix}_pop(); local b = {prefix}_pop(); {prefix}_push(b % a) end
    h[8] = function() local a = {prefix}_pop(); local b = {prefix}_pop(); {prefix}_push(b ^ a) end
    h[9] = function() local a = {prefix}_pop(); local b = {prefix}_pop(); {prefix}_push(b .. a) end
    h[10] = function() local a = {prefix}_pop(); local b = {prefix}_pop(); {prefix}_push(a == b) end
    h[11] = function() local a = {prefix}_pop(); local b = {prefix}_pop(); {prefix}_push(b < a) end
    h[12] = function() local a = {prefix}_pop(); local b = {prefix}_pop(); {prefix}_push(b > a) end
    h[13] = function() pc = code[pc] end
    h[14] = function() local t = code[pc]; pc = pc + 1; if not {prefix}_pop() then pc = t end end
    h[15] = function() local t = code[pc]; pc = pc + 1; if {prefix}_pop() then pc = t end end
    h[16] = function()
        local n = code[pc]; pc = pc + 1
        local args = {{}}
        for i = n, 1, -1 do args[i] = {prefix}_pop() end
        local f = {prefix}_pop()
        local saved = {{}}
        for k, v in pairs(regs) do saved[k] = v end
        local returns = {{f(unpack(args))}}
        for k, v in pairs(saved) do regs[k] = v end
        for i = #returns, 1, -1 do {prefix}_push(returns[i]) end
    end
    h[17] = function() pc = #code + 1 end
    h[18] = function() {prefix}_push(code[pc]); pc = pc + 1 end
    h[19] = function() {prefix}_push(nil) end
    h[20] = function() {prefix}_push(code[pc] == 1); pc = pc + 1 end
    h[21] = function() {prefix}_push(nil) end
    h[22] = function() local n = code[pc]; pc = pc + 1; _G[n] = {prefix}_pop() end
    h[23] = function() local n = code[pc]; pc = pc + 1; {prefix}_push(_G[n]) end
    h[24] = function() local i = code[pc]; pc = pc + 1; regs[i] = {prefix}_pop() end
    h[25] = function() local i = code[pc]; pc = pc + 1; {prefix}_push(regs[i]) end
    h[26] = function() {prefix}_push({{}}) end
    h[27] = function() local v = {prefix}_pop(); local k = {prefix}_pop(); local t = {prefix}_pop(); t[k] = v; {prefix}_push(t) end
    h[28] = function() local k = {prefix}_pop(); local t = {prefix}_pop(); {prefix}_push(t[k]) end
    h[29] = function() local a = {prefix}_pop(); local b = {prefix}_pop(); {prefix}_push(a ~= b) end
    h[30] = function() local a = {prefix}_pop(); local b = {prefix}_pop(); {prefix}_push(b <= a) end
    h[31] = function() local a = {prefix}_pop(); local b = {prefix}_pop(); {prefix}_push(b >= a) end
    h[32] = function() {prefix}_push(#{prefix}_pop()) end
    h[33] = function() {prefix}_push(-{prefix}_pop()) end
    h[34] = function() {prefix}_push(not {prefix}_pop()) end
    h[35] = function() local v = {prefix}_pop(); local t = {prefix}_pop(); t[#t + 1] = v; {prefix}_push(t) end
    h[36] = function() local i = code[pc]; pc = pc + 1; regs[i] = regs[i] + 1 end
    h[37] = function() local i = code[pc]; pc = pc + 1; regs[i] = (regs[i] or 0) - 1 end
    h[38] = function() local e = code[pc]; pc = pc + 1; if #code ~= e then error('integrity') end end
    while pc <= #code do
        local op = code[pc]
        pc = pc + 1
        local fn = h[op]
        if fn then fn() else break end
    end
end
end";

        // The shared VM uses globals so it's accessible from all functions/chunks
        var sharedRunName = $"{prefix}_run";

        return new FunctionCallStmt(
            new FunctionCallExpr(
                new FunctionCallExpr(new VarExpr("loadstring"))
                {
                    Arguments =
                    {
                        new LiteralExpr(LiteralExpr.LiteralKind.String, vmSrc),
                        new LiteralExpr(LiteralExpr.LiteralKind.String, $"=[{sharedRunName}]")
                    }
                }));
    }

    private static string NextHex(RandomNumberGenerator rng, int bytes)
    {
        var buf = new byte[bytes];
        rng.GetBytes(buf);
        return Convert.ToHexString(buf).ToLower();
    }

    private class VmGenerator
    {
        private readonly string _prefix;
        private readonly RandomNumberGenerator _rng;
        private readonly HashSet<string> _locals = new();
        private readonly Dictionary<string, int> _localRegs = new();
        private int _nextReg;
        private readonly Dictionary<string, int> _opMap;
        private readonly int _xorKey;

        // Use a fixed opcode mapping (same as the shared interpreter)
        // Per-function: only xor key and bytecode differ
        // Opcodes are FIXED to match the shared interpreter (handlers[0..38])
        private static readonly Dictionary<string, int> FixedOpMap = new()
        {
            ["NOP"] = 0,
            ["PUSH"] = 1,
            ["POP"] = 2,
            ["ADD"] = 3,
            ["SUB"] = 4,
            ["MUL"] = 5,
            ["DIV"] = 6,
            ["MOD"] = 7,
            ["POW"] = 8,
            ["CONCAT"] = 9,
            ["EQ"] = 10,
            ["LT"] = 11,
            ["GT"] = 12,
            ["JMP"] = 13,
            ["JZ"] = 14,
            ["JNZ"] = 15,
            ["CALL"] = 16,
            ["RET"] = 17,
            ["LOADK"] = 18,
            ["LOADN"] = 19,
            ["LOADB"] = 20,
            ["LOADNIL"] = 21,
            ["SETG"] = 22,
            ["GETG"] = 23,
            ["SETL"] = 24,
            ["GETL"] = 25,
            ["NEWTABLE"] = 26,
            ["SETTABLE"] = 27,
            ["GETTABLE"] = 28,
            ["NEQ"] = 29,
            ["LEQ"] = 30,
            ["GEQ"] = 31,
            ["LEN"] = 32,
            ["NEG"] = 33,
            ["NOT"] = 34,
            ["APPEND"] = 35,
            ["INC"] = 36,
            ["DBG_CHECK"] = 37,
            ["INTEG_CHECK"] = 38,
        };

        public VmGenerator(string prefix, RandomNumberGenerator rng, string xorKeyHex)
        {
            _prefix = prefix;
            _rng = rng;
            _xorKey = Convert.ToInt32(xorKeyHex, 16);
            _opMap = FixedOpMap;
        }

        private int Op(string name) => _opMap[name];
        private long OpL(string name) => (long)_opMap[name];

        private int GetReg(string name)
        {
            if (!_localRegs.TryGetValue(name, out var reg))
            {
                reg = _nextReg++;
                _localRegs[name] = reg;
            }
            return reg;
        }

        public Statement VirtualizeFunction(FunctionDeclStmt funcDecl)
        {
            var bytecode = new List<object>();
            _locals.Clear();
            _localRegs.Clear();
            _nextReg = 0;

            // Add function name to locals (for recursive calls)
            if (funcDecl.Name != null)
            {
                _locals.Add(funcDecl.Name);
                GetReg(funcDecl.Name);
            }

            // Add function parameters as locals
            foreach (var param in funcDecl.FuncExpr.Parameters)
            {
                _locals.Add(param);
                GetReg(param);
            }

            CompileBlock(funcDecl.FuncExpr.Body, bytecode);

            // Add integrity check at end
            bytecode.Add(OpL("INTEG_CHECK"));
            bytecode.Add((long)bytecode.Count + 1);

            // Encrypt bytecode
            var encrypted = new List<object>();
            foreach (var instr in bytecode)
            {
                if (instr is long l)
                    encrypted.Add(l - _xorKey);
                else
                    encrypted.Add(instr);
            }

            var bytecodeTable = new TableConstructorExpr();
            foreach (var instr in encrypted)
            {
                if (instr is long l)
                    bytecodeTable.Fields.Add(new TableField
                    {
                        Value = new LiteralExpr(LiteralExpr.LiteralKind.Number, l)
                    });
                else if (instr is string s)
                    bytecodeTable.Fields.Add(new TableField
                    {
                        Value = new LiteralExpr(LiteralExpr.LiteralKind.String, s)
                    });
                else if (instr is bool b)
                    bytecodeTable.Fields.Add(new TableField
                    {
                        Value = new LiteralExpr(LiteralExpr.LiteralKind.Boolean, b)
                    });
            }



            // Use the shared VM: __lgsvm1_run(code, key)
            var sharedRunName = $"__lgsvm1_run";
            var sharedStkName = $"__lgsvm1_stk";
            var sharedRegsName = $"__lgsvm1_regs";
            var sharedPopName = $"__lgsvm1_pop";

            var codeLocal = $"{_prefix}_c";
            var keyLocal = $"{_prefix}_k";
            var regsLocal = $"{_prefix}_r";

            // Build the function: 
            // local code = {...}
            // local key = <xorKey>
            // __lgsvm1_regs[1] = param1 (for each parameter)
            // <sharedRunName>(code, key)
            // return __lgsvm1_pop()
            var block = new BlockStmt();

            block.Statements.Add(new LocalVarStmt
            {
                Names = { codeLocal },
                Values = { bytecodeTable }
            });

            block.Statements.Add(new LocalVarStmt
            {
                Names = { keyLocal },
                Values = { new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)_xorKey) }
            });

            // Store function name into its own register (for recursive calls)
            if (funcDecl.Name != null)
            {
                block.Statements.Add(new AssignmentStmt
                {
                    Targets =
                    {
                        new IndexExpr(
                            new VarExpr(sharedRegsName),
                            new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)GetReg(funcDecl.Name)))
                    },
                    Values = { new VarExpr(funcDecl.Name) }
                });
            }

            // Store parameters into shared VM registers
            foreach (var param in funcDecl.FuncExpr.Parameters)
            {
                block.Statements.Add(new AssignmentStmt
                {
                    Targets =
                    {
                        new IndexExpr(
                            new VarExpr(sharedRegsName),
                            new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)GetReg(param)))
                    },
                    Values = { new VarExpr(param) }
                });
            }

            block.Statements.Add(new FunctionCallStmt(
                new FunctionCallExpr(new VarExpr(sharedRunName))
                {
                    Arguments =
                    {
                        new VarExpr(codeLocal),
                        new VarExpr(keyLocal)
                    }
                }));

            // Pop all VM return values from the shared stack (stack is LIFO, reverse to preserve order)
            var retNLocal = $"{_prefix}_n";
            var retTLocal = $"{_prefix}_t";
            block.Statements.Add(new LocalVarStmt
            {
                Names = { retNLocal },
                Values = { new VarExpr("__lgsvm1_sp") }
            });
            var ifBlock = new BlockStmt();
            ifBlock.Statements.Add(new LocalVarStmt
            {
                Names = { retTLocal },
                Values = { new TableConstructorExpr() }
            });
            var forBody = new BlockStmt();
            forBody.Statements.Add(new AssignmentStmt
            {
                Targets =
                {
                    new IndexExpr(
                        new VarExpr(retTLocal),
                        new VarExpr($"{_prefix}_i"))
                },
                Values =
                {
                    new FunctionCallExpr(new VarExpr(sharedPopName))
                }
            });
            ifBlock.Statements.Add(new ForNumericStmt
            {
                VarName = $"{_prefix}_i",
                Start = new VarExpr(retNLocal),
                End = new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)1),
                Step = new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)-1),
                Body = forBody
            });
            var unpackCall = new FunctionCallExpr(new VarExpr("unpack"));
            unpackCall.Arguments.Add(new VarExpr(retTLocal));
            ifBlock.Statements.Add(new ReturnStmt
            {
                Values = { unpackCall }
            });
            block.Statements.Add(new IfStmt());
            var ifStmt = (IfStmt)block.Statements[^1];
            ifStmt.Branches.Add((
                new BinaryExpr(BinaryOp.Geq, new VarExpr(retNLocal), new LiteralExpr(LiteralExpr.LiteralKind.Number, (long)1)),
                ifBlock));

            // Now wrap this in a function with the same name/signature
            var funcExpr = new FuncDeclExpr();
            foreach (var param in funcDecl.FuncExpr.Parameters)
                funcExpr.Parameters.Add(param);
            funcExpr.IsVararg = funcDecl.FuncExpr.IsVararg;
            funcExpr.Body = block;

            var funcCallExpr = new FunctionCallExpr(funcExpr);
            foreach (var p in funcDecl.FuncExpr.Parameters)
            {
                // The function returns nothing, so the call result is discarded
            }
            funcCallExpr.Arguments.Add(new VarargsExpr());

            var result = new BlockStmt();

            var funcName = funcDecl.Name ?? $"vm_{NextHex(_rng, 8)}";
            var newFuncStmt = new FunctionDeclStmt
            {
                Name = funcName,
                NamePrefix = funcDecl.NamePrefix != null ? new List<string>(funcDecl.NamePrefix) : null,
                IsLocal = funcDecl.IsLocal,
                IsMethod = funcDecl.IsMethod,
                FuncExpr = funcExpr
            };

            result.Statements.Add(newFuncStmt);

            return result;
        }

        private void CompileBlock(BlockStmt block, List<object> bc)
        {
            var savedLocals = new HashSet<string>(_locals);
            foreach (var stmt in block.Statements)
                CompileStatement(stmt, bc);
            _locals.Clear();
            foreach (var l in savedLocals)
                _locals.Add(l);
        }

        private void CompileStatement(Statement stmt, List<object> bc)
        {
            switch (stmt)
            {
                case LocalVarStmt l:
                    for (var i = l.Values.Count - 1; i >= 0; i--)
                        CompileExpr(l.Values[i], bc);
                    if (l.Values.Count < l.Names.Count)
                    {
                        for (var i = l.Values.Count; i < l.Names.Count; i++)
                            bc.Add(OpL("LOADNIL"));
                    }
                    var assignCount2 = Math.Min(l.Names.Count, Math.Max(l.Values.Count, 1));
                    for (var i = 0; i < assignCount2; i++)
                    {
                        var name = l.Names[i];
                        _locals.Add(name);
                        bc.Add(OpL("SETL")); bc.Add((long)GetReg(name));
                    }
                    if (l.Values.Count > l.Names.Count)
                    {
                        var extra = l.Values.Count - l.Names.Count;
                        for (var i = 0; i < extra; i++)
                            bc.Add(OpL("POP"));
                    }
                    break;

                case ReturnStmt r:
                    foreach (var v in r.Values)
                        CompileExpr(v, bc);
                    bc.Add(OpL("RET"));
                    break;

                case FunctionCallStmt fc:
                    CompileExpr(fc.Call, bc);
                    bc.Add(OpL("POP"));
                    break;

                case IfStmt i:
                    var branchEndPatches = new List<int>();
                    for (var j = 0; j < i.Branches.Count; j++)
                    {
                        var branch = i.Branches[j];
                        CompileExpr(branch.Condition, bc);
                        var jmpzPatch = bc.Count;
                        bc.Add(OpL("JZ")); bc.Add(0L);
                        CompileBlock(branch.Body, bc);
                        if (j < i.Branches.Count - 1 || i.ElseBody != null)
                        {
                            branchEndPatches.Add(bc.Count);
                            bc.Add(OpL("JMP")); bc.Add(0L);
                        }
                        bc[jmpzPatch + 1] = (long)bc.Count + 1;
                    }
                    if (i.ElseBody != null)
                        CompileBlock(i.ElseBody, bc);
                    foreach (var patch in branchEndPatches)
                        bc[patch + 1] = (long)bc.Count + 1;
                    break;

                case WhileStmt w:
                    var loopStart = bc.Count;
                    CompileExpr(w.Condition, bc);
                    var exitPatch = bc.Count;
                    bc.Add(OpL("JZ")); bc.Add(0L);
                    CompileBlock(w.Body, bc);
                    bc.Add(OpL("JMP")); bc.Add((long)loopStart + 1);
                    bc[exitPatch + 1] = (long)bc.Count + 1;
                    break;

                case RepeatStmt r:
                    var repeatStart = bc.Count;
                    CompileBlock(r.Body, bc);
                    CompileExpr(r.Condition, bc);
                    var jzPatch = bc.Count;
                    bc.Add(OpL("JZ")); bc.Add(0L);
                    bc.Add(OpL("JMP")); bc.Add((long)repeatStart + 1);
                    bc[jzPatch + 1] = (long)bc.Count + 1;
                    break;

                case ForNumericStmt fn:
                    _locals.Add(fn.VarName);
                    var varReg = GetReg(fn.VarName);
                    var endReg = _nextReg++;
                    var stepReg = _nextReg++;
                    CompileExpr(fn.Start, bc);
                    bc.Add(OpL("SETL")); bc.Add((long)varReg);
                    CompileExpr(fn.End, bc);
                    bc.Add(OpL("SETL")); bc.Add((long)endReg);
                    if (fn.Step != null)
                    {
                        CompileExpr(fn.Step, bc);
                        bc.Add(OpL("SETL")); bc.Add((long)stepReg);
                    }
                    var forLoopStart = bc.Count;
                    bc.Add(OpL("GETL")); bc.Add((long)varReg);
                    bc.Add(OpL("GETL")); bc.Add((long)endReg);
                    bc.Add(OpL("LEQ"));
                    var forExit = bc.Count;
                    bc.Add(OpL("JZ")); bc.Add(0L);
                    CompileBlock(fn.Body, bc);
                    bc.Add(OpL("INC")); bc.Add((long)varReg);
                    bc.Add(OpL("JMP")); bc.Add((long)forLoopStart + 1);
                    bc[forExit + 1] = (long)bc.Count + 1;
                    break;

                case ForGenericStmt fg:
                    foreach (var it in fg.Iterators)
                        CompileExpr(it, bc);
                    bc.Add(OpL("CALL")); bc.Add((long)fg.Iterators.Count);
                    foreach (var vn in fg.VarNames)
                        _locals.Add(vn);
                    for (var i = 0; i < fg.VarNames.Count; i++)
                    {
                        bc.Add(OpL("SETL")); bc.Add((long)i);
                    }
                    CompileBlock(fg.Body, bc);
                    break;

                case DoStmt d:
                    CompileBlock(d.Body, bc);
                    break;

                case GotoStmt:
                    bc.Add(OpL("NOP"));
                    break;

                case LabelStmt:
                    bc.Add(OpL("NOP"));
                    break;

                case FunctionDeclStmt:
                    bc.Add(OpL("NOP"));
                    break;

                case AssignmentStmt a:
                    foreach (var t in a.Targets)
                    {
                        if (t is IndexExpr ix)
                        {
                            CompileExpr(ix.Object, bc);
                            CompileExpr(ix.Index, bc);
                        }
                        else if (t is MemberExpr me)
                        {
                            CompileExpr(me.Object, bc);
                            bc.Add(OpL("LOADK"));
                            bc.Add(me.Member);
                        }
                    }
                    foreach (var v in a.Values)
                        CompileExpr(v, bc);
                    foreach (var t in a.Targets)
                    {
                        if (t is VarExpr ve)
                        {
                            if (_locals.Contains(ve.Name))
                            {
                                bc.Add(OpL("SETL")); bc.Add((long)GetReg(ve.Name));
                            }
                            else
                            {
                                bc.Add(OpL("SETG"));
                                bc.Add(ve.Name);
                            }
                        }
                        else
                        {
                            bc.Add(OpL("SETTABLE"));
                        }
                    }
                    break;

                default:
                    throw new NotSupportedException($"Unsupported statement: {stmt?.GetType().Name}");
            }
        }

        private void CompileExpr(Expression expr, List<object> bc)
        {
            switch (expr)
            {
                case LiteralExpr l when l.Kind == LiteralExpr.LiteralKind.Nil:
                    bc.Add(OpL("LOADNIL"));
                    break;

                case LiteralExpr l when l.Kind == LiteralExpr.LiteralKind.Boolean:
                    bc.Add(OpL("LOADB"));
                    bc.Add((bool)l.Value! ? 1L : 0L);
                    break;

                case LiteralExpr l when l.Kind == LiteralExpr.LiteralKind.Number:
                    bc.Add(OpL("LOADK"));
                    var val = l.Value;
                    // Ensure boxed as long so the encryption step encodes it (some passes may produce doubles)
                    if (val is double d && d == Math.Truncate(d))
                        val = (long)d;
                    bc.Add(val!);
                    break;

                case LiteralExpr l when l.Kind == LiteralExpr.LiteralKind.String:
                    bc.Add(OpL("LOADK"));
                    bc.Add(l.Value!);
                    break;

                case VarExpr v:
                    if (_locals.Contains(v.Name))
                    {
                        bc.Add(OpL("GETL"));
                        bc.Add((long)GetReg(v.Name));
                    }
                    else
                    {
                        bc.Add(OpL("GETG"));
                        bc.Add(v.Name);
                    }
                    break;

                case BinaryExpr b:
                    if (b.Op is BinaryOp.And or BinaryOp.Or)
                    {
                        CompileExpr(b.Left, bc);
                        var dupPatch = bc.Count;
                        bc.Add(OpL("JNZ")); bc.Add(0L);
                        CompileExpr(b.Right, bc);
                        bc[dupPatch + 1] = (long)bc.Count + 1;
                    }
                    else
                    {
                        CompileExpr(b.Left, bc);
                        CompileExpr(b.Right, bc);
                        bc.Add(b.Op switch
                        {
                            BinaryOp.Add => OpL("ADD"), BinaryOp.Subtract => OpL("SUB"),
                            BinaryOp.Multiply => OpL("MUL"), BinaryOp.Divide => OpL("DIV"),
                            BinaryOp.Modulo => OpL("MOD"), BinaryOp.Power => OpL("POW"),
                            BinaryOp.Concat => OpL("CONCAT"),
                            BinaryOp.Eq => OpL("EQ"), BinaryOp.Neq => OpL("NEQ"),
                            BinaryOp.Lt => OpL("LT"), BinaryOp.Gt => OpL("GT"),
                            BinaryOp.Leq => OpL("LEQ"), BinaryOp.Geq => OpL("GEQ"),
                            _ => throw new NotSupportedException()
                        });
                    }
                    break;

                case UnaryExpr u:
                    if (u.Op == UnaryOp.Length)
                    { CompileExpr(u.Operand, bc); bc.Add(OpL("LEN")); }
                    else if (u.Op == UnaryOp.Negate)
                    { CompileExpr(u.Operand, bc); bc.Add(OpL("NEG")); }
                    else
                    {
                        CompileExpr(u.Operand, bc);
                        bc.Add(OpL("NOT"));
                    }
                    break;

                case MemberExpr m:
                    CompileExpr(m.Object, bc);
                    bc.Add(OpL("GETTABLE"));
                    bc.Add(m.Member);
                    break;

                case TableConstructorExpr t:
                    bc.Add(OpL("NEWTABLE"));
                    foreach (var f in t.Fields)
                    {
                        if (f.Key != null)
                        {
                            CompileExpr(f.Key, bc);
                            CompileExpr(f.Value, bc);
                            bc.Add(OpL("SETTABLE"));
                        }
                        else
                        {
                            CompileExpr(f.Value, bc);
                            bc.Add(OpL("APPEND"));
                        }
                    }
                    break;

                case FunctionCallExpr fc:
                    if (fc.Callee is VarExpr fv)
                    {
                        if (_locals.Contains(fv.Name))
                        {
                            bc.Add(OpL("GETL"));
                            bc.Add((long)GetReg(fv.Name));
                        }
                        else
                        {
                            bc.Add(OpL("GETG"));
                            bc.Add(fv.Name);
                        }
                    }
                    else if (fc.Callee is MemberExpr me)
                    {
                        CompileExpr(me.Object, bc);
                        bc.Add(OpL("GETTABLE"));
                        bc.Add(me.Member);
                        if (fc.IsMethodCall)
                            CompileExpr(me.Object, bc);
                    }
                    foreach (var a in fc.Arguments)
                        CompileExpr(a, bc);
                    var nargs = fc.Arguments.Count + (fc.IsMethodCall ? 1L : 0L);
                    bc.Add(OpL("CALL"));
                    bc.Add(nargs);
                    break;

                case IndexExpr ix:
                    CompileExpr(ix.Object, bc);
                    CompileExpr(ix.Index, bc);
                    bc.Add(OpL("GETTABLE"));
                    break;

                case ConcatExpr cx:
                    if (cx.Parts.Count == 0)
                    {
                        bc.Add(OpL("LOADK"));
                        bc.Add("");
                    }
                    else
                    {
                        CompileExpr(cx.Parts[0], bc);
                        for (var k = 1; k < cx.Parts.Count; k++)
                        {
                            CompileExpr(cx.Parts[k], bc);
                            bc.Add(OpL("CONCAT"));
                        }
                    }
                    break;

                case VarargsExpr:
                    bc.Add(OpL("LOADK"));
                    bc.Add(0L);
                    break;

                case FuncDeclExpr:
                    bc.Add(OpL("LOADK"));
                    bc.Add("<func>");
                    break;

                default:
                    throw new NotSupportedException($"Unsupported expression: {expr?.GetType().Name}");
            }
        }

    }
}
