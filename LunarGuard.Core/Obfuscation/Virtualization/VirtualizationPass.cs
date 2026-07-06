using System.Security.Cryptography;
using System.Text;
using LunarGuard.Core.AST;
using LunarGuard.Core.AST.Stmt;
using LunarGuard.Core.AST.Expr;

namespace LunarGuard.Core.Obfuscation.Virtualization;

public class VirtualizationPass : IObfuscationPass
{
    public string Name => "Bytecode Virtualization";

    public void Transform(BlockStmt root, ObfuscationOptions options)
    {
        if (!options.Virtualize) return;

        var rng = RandomNumberGenerator.Create();
        var vmPrefix = $"vm_{NextHex(rng, 8)}";
        var xorKey = NextHex(rng, 4); // 16-bit key for bytecode encryption
        var vmm = new VmGenerator(vmPrefix, rng, xorKey);

        var statements = root.Statements.ToList();
        var vmFunctions = new List<(Statement Stmt, Statement Original)>();

        foreach (var stmt in statements)
        {
            if (stmt is FunctionDeclStmt fd)
            {
                var virtualized = vmm.VirtualizeFunction(fd);
                vmFunctions.Add((virtualized, stmt));
            }
        }

        foreach (var (vStmt, original) in vmFunctions)
        {
            var idx = root.Statements.IndexOf(original);
            root.Statements.Insert(idx, vStmt);
            root.Statements.Remove(original);
        }

        root.Statements.Insert(0, CreateVMInterpreter(vmPrefix, rng, xorKey));
    }

    private static string NextHex(RandomNumberGenerator rng, int bytes)
    {
        var buf = new byte[bytes];
        rng.GetBytes(buf);
        return Convert.ToHexString(buf).ToLower();
    }

    private static Statement CreateVMInterpreter(string prefix, RandomNumberGenerator rng, string xorKeyHex)
    {
        var codeName = $"{prefix}_code";
        var stackName = $"{prefix}_stk";
        var regsName = $"{prefix}_regs";
        var keyName = $"{prefix}_key";
        var integName = $"{prefix}_integ";

        var xorKeyInt = Convert.ToInt32(xorKeyHex, 16);

        // Random opcode permutation for this build
        var opcodes = Enumerable.Range(0, 44).OrderBy(_ => { var b = new byte[1]; rng.GetBytes(b); return b[0]; }).ToList();

        var opNames = new Dictionary<string, int>
        {
            ["NOP"] = opcodes[0],
            ["PUSH"] = opcodes[1],
            ["POP"] = opcodes[2],
            ["ADD"] = opcodes[3],
            ["SUB"] = opcodes[4],
            ["MUL"] = opcodes[5],
            ["DIV"] = opcodes[6],
            ["MOD"] = opcodes[7],
            ["POW"] = opcodes[8],
            ["CONCAT"] = opcodes[9],
            ["EQ"] = opcodes[10],
            ["LT"] = opcodes[11],
            ["GT"] = opcodes[12],
            ["JMP"] = opcodes[13],
            ["JZ"] = opcodes[14],
            ["JNZ"] = opcodes[15],
            ["CALL"] = opcodes[16],
            ["RET"] = opcodes[17],
            ["LOADK"] = opcodes[18],
            ["LOADN"] = opcodes[19],
            ["LOADB"] = opcodes[20],
            ["LOADNIL"] = opcodes[21],
            ["SETG"] = opcodes[22],
            ["GETG"] = opcodes[23],
            ["SETL"] = opcodes[24],
            ["GETL"] = opcodes[25],
            ["NEWTABLE"] = opcodes[26],
            ["SETTABLE"] = opcodes[27],
            ["GETTABLE"] = opcodes[28],
            ["NEQ"] = opcodes[29],
            ["LEQ"] = opcodes[30],
            ["GEQ"] = opcodes[31],
            ["LEN"] = opcodes[32],
            ["NEG"] = opcodes[33],
            ["NOT"] = opcodes[34],
            ["APPEND"] = opcodes[35],
            ["INC"] = opcodes[36],
            ["DBG_CHECK"] = opcodes[37],
            ["INTEG_CHECK"] = opcodes[38],
        };

        var antiDebugBuf = new byte[8];
        rng.GetBytes(antiDebugBuf);
        var dbgConst1 = BitConverter.ToInt32(antiDebugBuf, 0) & 0x7FFFFFFF;
        var dbgConst2 = BitConverter.ToInt32(antiDebugBuf, 4) & 0x7FFFFFFF;

        var integSeed = new byte[8];
        rng.GetBytes(integSeed);
        var integCheckVal = BitConverter.ToInt64(integSeed, 0);

        var vmSrc = $@"
local {codeName} = ...
local {stackName} = {{}}
local {regsName} = {{}}
local pc = 1
local sp = 0
local dbg_flag = false

-- Decrypt bytecode
local {keyName} = {xorKeyInt}
for i = 1, #{codeName} do
    local v = {codeName}[i]
    if type(v) == 'number' and v == math.floor(v) and v >= 0 then
        {codeName}[i] = v + {keyName}
    end
end

-- Integrity seed
local {integName} = {integCheckVal}

local function peek()
    return {stackName}[sp]
end

local function push(v)
    sp = sp + 1
    {stackName}[sp] = v
end

local function pop()
    local v = {stackName}[sp]
    {stackName}[sp] = nil
    sp = sp - 1
    return v
end

-- Anti-debug check (runtime)
local function check_dbg()
    if dbg_flag then return end
    local ok, info = pcall(debug.getinfo, 0)
    if ok and info then
        dbg_flag = true
    end
end

-- Dispatch table (shuffled per build)
local handlers = {{}}

handlers[{opNames["PUSH"]}] = function()
    push({codeName}[pc]); pc = pc + 1
end

handlers[{opNames["POP"]}] = function()
    pop()
end

handlers[{opNames["ADD"]}] = function()
    local a = pop(); local b = pop(); push(a + b)
end

handlers[{opNames["SUB"]}] = function()
    local a = pop(); local b = pop(); push(b - a)
end

handlers[{opNames["MUL"]}] = function()
    local a = pop(); local b = pop(); push(a * b)
end

handlers[{opNames["DIV"]}] = function()
    local a = pop(); local b = pop(); push(b / a)
end

handlers[{opNames["MOD"]}] = function()
    local a = pop(); local b = pop(); push(b % a)
end

handlers[{opNames["POW"]}] = function()
    local a = pop(); local b = pop(); push(b ^ a)
end

handlers[{opNames["CONCAT"]}] = function()
    local a = pop(); local b = pop(); push(b .. a)
end

handlers[{opNames["EQ"]}] = function()
    local a = pop(); local b = pop(); push(a == b)
end

handlers[{opNames["LT"]}] = function()
    local a = pop(); local b = pop(); push(a < b)
end

handlers[{opNames["GT"]}] = function()
    local a = pop(); local b = pop(); push(a > b)
end

handlers[{opNames["JMP"]}] = function()
    pc = {codeName}[pc]
end

handlers[{opNames["JZ"]}] = function()
    local target = {codeName}[pc]; pc = pc + 1
    if not pop() then pc = target end
end

handlers[{opNames["JNZ"]}] = function()
    local target = {codeName}[pc]; pc = pc + 1
    if pop() then pc = target end
end

handlers[{opNames["CALL"]}] = function()
    local nargs = {codeName}[pc]; pc = pc + 1
    local func = pop()
    local args = {{}}
    for i = 1, nargs do
        args[i] = pop()
    end
    local results = table.pack(func(table.unpack(args)))
    for i = #results, 1, -1 do
        push(results[i])
    end
end

handlers[{opNames["RET"]}] = function()
    -- signal to exit main loop
    pc = #{codeName} + 1
end

handlers[{opNames["LOADK"]}] = function()
    push({codeName}[pc]); pc = pc + 1
end

handlers[{opNames["LOADN"]}] = function()
    push(nil)
end

handlers[{opNames["LOADB"]}] = function()
    if {codeName}[pc] == 1 then push(true) else push(false) end; pc = pc + 1
end

handlers[{opNames["LOADNIL"]}] = function()
    push(nil)
end

handlers[{opNames["SETG"]}] = function()
    local name = {codeName}[pc]; pc = pc + 1
    vm_env[name] = pop()
end

handlers[{opNames["GETG"]}] = function()
    local name = {codeName}[pc]; pc = pc + 1
    push(vm_env[name])
end

handlers[{opNames["SETL"]}] = function()
    local idx = {codeName}[pc]; pc = pc + 1
    {regsName}[idx] = pop()
end

handlers[{opNames["GETL"]}] = function()
    local idx = {codeName}[pc]; pc = pc + 1
    push({regsName}[idx])
end

handlers[{opNames["NEWTABLE"]}] = function()
    push({{}})
end

handlers[{opNames["SETTABLE"]}] = function()
    local val = pop(); local key = pop(); local tbl = pop()
    tbl[key] = val
    push(tbl)
end

handlers[{opNames["GETTABLE"]}] = function()
    local key = pop(); local tbl = pop()
    push(tbl[key])
end

handlers[{opNames["NEQ"]}] = function()
    local a = pop(); local b = pop(); push(a ~= b)
end

handlers[{opNames["LEQ"]}] = function()
    local a = pop(); local b = pop(); push(a <= b)
end

handlers[{opNames["GEQ"]}] = function()
    local a = pop(); local b = pop(); push(a >= b)
end

handlers[{opNames["LEN"]}] = function()
    push(#pop())
end

handlers[{opNames["NEG"]}] = function()
    push(-pop())
end

handlers[{opNames["NOT"]}] = function()
    push(not pop())
end

handlers[{opNames["APPEND"]}] = function()
    local val = pop(); local tbl = pop()
    tbl[#tbl + 1] = val
    push(tbl)
end

handlers[{opNames["INC"]}] = function()
    local idx = {codeName}[pc]; pc = pc + 1
    {regsName}[idx] = {regsName}[idx] + 1
end

handlers[{opNames["DBG_CHECK"]}] = function()
    check_dbg()
    if dbg_flag then
        -- Soft-fail: corrupt the VM state subtly
        local idx = {codeName}[pc]; pc = pc + 1
        {regsName}[idx] = ({regsName}[idx] or 0) - {dbgConst1}
    end
end

handlers[{opNames["INTEG_CHECK"]}] = function()
    local expected = {codeName}[pc]; pc = pc + 1
    local actual = #{codeName}
    if actual ~= expected then
        -- Integrity failure: corrupt
        {integName} = {integName} - actual
    end
end

handlers[{opNames["NOP"]}] = function()
    -- no-op (used for random gaps)
end

-- Main loop
while pc <= #{codeName} do
    local op = {codeName}[pc]
    pc = pc + 1
    local h = handlers[op]
    if h then
        h()
    else
        break
    end
end";

        return new AssignmentStmt
        {
            Targets = { new VarExpr($"{prefix}_run") },
            Values =
            {
                new FunctionCallExpr(new VarExpr("loadstring"))
                {
                    Arguments =
                    {
                        new LiteralExpr(LiteralExpr.LiteralKind.String, vmSrc)
                    }
                }
            }
        };
    }

    private class VmGenerator
    {
        private readonly string _prefix;
        private readonly RandomNumberGenerator _rng;
        private readonly HashSet<string> _locals = new();
        private readonly HashSet<string> _globalRefs = new();
        private readonly Dictionary<string, int> _opMap;
        private readonly int _xorKey;
        private readonly int _nopOp;
        private readonly int _dbgCheckOp;
        private readonly int _integCheckOp;

        public VmGenerator(string prefix, RandomNumberGenerator rng, string xorKeyHex)
        {
            _prefix = prefix;
            _rng = rng;

            // Build opcode mapping matching the interpreter
            var opcodes = Enumerable.Range(0, 44).OrderBy(_ => { var b = new byte[1]; rng.GetBytes(b); return b[0]; }).ToList();
            _opMap = new Dictionary<string, int>
            {
                ["NOP"] = opcodes[0],
                ["PUSH"] = opcodes[1],
                ["POP"] = opcodes[2],
                ["ADD"] = opcodes[3],
                ["SUB"] = opcodes[4],
                ["MUL"] = opcodes[5],
                ["DIV"] = opcodes[6],
                ["MOD"] = opcodes[7],
                ["POW"] = opcodes[8],
                ["CONCAT"] = opcodes[9],
                ["EQ"] = opcodes[10],
                ["LT"] = opcodes[11],
                ["GT"] = opcodes[12],
                ["JMP"] = opcodes[13],
                ["JZ"] = opcodes[14],
                ["JNZ"] = opcodes[15],
                ["CALL"] = opcodes[16],
                ["RET"] = opcodes[17],
                ["LOADK"] = opcodes[18],
                ["LOADN"] = opcodes[19],
                ["LOADB"] = opcodes[20],
                ["LOADNIL"] = opcodes[21],
                ["SETG"] = opcodes[22],
                ["GETG"] = opcodes[23],
                ["SETL"] = opcodes[24],
                ["GETL"] = opcodes[25],
                ["NEWTABLE"] = opcodes[26],
                ["SETTABLE"] = opcodes[27],
                ["GETTABLE"] = opcodes[28],
                ["NEQ"] = opcodes[29],
                ["LEQ"] = opcodes[30],
                ["GEQ"] = opcodes[31],
                ["LEN"] = opcodes[32],
                ["NEG"] = opcodes[33],
                ["NOT"] = opcodes[34],
                ["APPEND"] = opcodes[35],
                ["INC"] = opcodes[36],
                ["DBG_CHECK"] = opcodes[37],
                ["INTEG_CHECK"] = opcodes[38],
            };
            _nopOp = _opMap["NOP"];
            _dbgCheckOp = _opMap["DBG_CHECK"];
            _integCheckOp = _opMap["INTEG_CHECK"];
            _xorKey = Convert.ToInt32(xorKeyHex, 16);
        }

        private int Op(string name) => _opMap[name];
        private long OpL(string name) => (long)_opMap[name];

        public Statement VirtualizeFunction(FunctionDeclStmt funcDecl)
        {
            var bytecode = new List<object>();
            CompileBlock(funcDecl.FuncExpr.Body, bytecode);

            // Insert anti-debug check at entry
            var injectedBytecode = new List<object> { OpL("DBG_CHECK"), 0L };
            injectedBytecode.AddRange(bytecode);
            // Add integrity check at function end
            var expectedLength = injectedBytecode.Count + 1;
            injectedBytecode.Add(OpL("INTEG_CHECK"));
            injectedBytecode.Add((long)expectedLength);

            // Insert random NOPs for obfuscation
            var nopCount = (byte)(injectedBytecode.Count * 0.05); // ~5% NOPs
            var nopBuf = new byte[1];
            for (var i = 0; i < nopCount; i++)
            {
                _rng.GetBytes(nopBuf);
                var pos = nopBuf[0] % (injectedBytecode.Count + 1);
                injectedBytecode.Insert(pos, OpL("NOP"));
            }

            // Encrypt bytecode (add offset for Lua 5.1 compatibility)
            var encrypted = new List<object>();
            foreach (var instr in injectedBytecode)
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

            var vmRunName = $"{_prefix}_run";
            var funcName = funcDecl.Name ?? $"vm_{NextHex(_rng, 8)}";
            var vmCall = new FunctionCallExpr(
                new FunctionCallExpr(new VarExpr("loadstring"))
                {
                    Arguments =
                    {
                        new LiteralExpr(LiteralExpr.LiteralKind.String,
                            "return " + vmRunName + "(...)"),
                        new LiteralExpr(LiteralExpr.LiteralKind.String, "=" + (funcDecl.Name ?? "anon"))
                    }
                })
            {
                Arguments = { bytecodeTable }
            };

            if (funcDecl.NamePrefix != null && funcDecl.NamePrefix.Count > 0)
            {
                Expression table = new VarExpr(funcDecl.NamePrefix[0]);
                for (var i = 1; i < funcDecl.NamePrefix.Count; i++)
                    table = new IndexExpr(table, new LiteralExpr(LiteralExpr.LiteralKind.String, funcDecl.NamePrefix[i]));

                Expression target;
                if (funcDecl.IsMethod)
                    target = new IndexExpr(table, new LiteralExpr(LiteralExpr.LiteralKind.String, funcDecl.Name));
                else
                    target = new IndexExpr(table, new LiteralExpr(LiteralExpr.LiteralKind.String, funcName));

                return new AssignmentStmt
                {
                    Targets = { target },
                    Values = { vmCall }
                };
            }

            return new LocalVarStmt
            {
                Names = { funcName },
                Values = { vmCall }
            };
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
                    var assignCount = Math.Min(l.Names.Count, Math.Max(l.Values.Count, 1));
                    for (var i = 0; i < assignCount; i++)
                    {
                        bc.Add(OpL("SETL")); bc.Add((long)i);
                    }
                    if (l.Values.Count > l.Names.Count)
                    {
                        var extra = l.Values.Count - l.Names.Count;
                        for (var i = 0; i < extra; i++)
                            bc.Add(OpL("POP"));
                    }
                    foreach (var name in l.Names)
                        _locals.Add(name);
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
                        var (cond, body) = i.Branches[j];
                        CompileExpr(cond, bc);
                        var jmpzPatch = bc.Count;
                        bc.Add(OpL("JZ")); bc.Add(0L);
                        CompileBlock(body, bc);
                        if (j < i.Branches.Count - 1 || i.ElseBody != null)
                        {
                            branchEndPatches.Add(bc.Count);
                            bc.Add(OpL("JMP")); bc.Add(0L);
                        }
                        bc[jmpzPatch + 1] = (long)bc.Count;
                    }
                    if (i.ElseBody != null)
                        CompileBlock(i.ElseBody, bc);
                    foreach (var patch in branchEndPatches)
                        bc[patch + 1] = (long)bc.Count;
                    break;

                case WhileStmt w:
                    var loopStart = bc.Count;
                    CompileExpr(w.Condition, bc);
                    var exitPatch = bc.Count;
                    bc.Add(OpL("JZ")); bc.Add(0L);
                    CompileBlock(w.Body, bc);
                    bc.Add(OpL("JMP")); bc.Add((long)loopStart);
                    bc[exitPatch + 1] = (long)bc.Count;
                    break;

                case ForNumericStmt fn:
                    _locals.Add(fn.VarName);
                    CompileExpr(fn.Start, bc);
                    bc.Add(OpL("SETL")); bc.Add(0L);
                    CompileExpr(fn.End, bc);
                    bc.Add(OpL("SETL")); bc.Add(1L);
                    if (fn.Step != null)
                    {
                        CompileExpr(fn.Step, bc);
                        bc.Add(OpL("SETL")); bc.Add(2L);
                    }
                    var forLoopStart = bc.Count;
                    bc.Add(OpL("GETL")); bc.Add(0L);
                    bc.Add(OpL("GETL")); bc.Add(1L);
                    bc.Add(OpL("LEQ"));
                    var forExit = bc.Count;
                    bc.Add(OpL("JZ")); bc.Add(0L);
                    foreach (var name in new[] { fn.VarName })
                    {
                        if (_globalRefs.Contains(name)) continue;
                    }
                    CompileBlock(fn.Body, bc);
                    bc.Add(OpL("INC")); bc.Add(0L);
                    bc.Add(OpL("JMP")); bc.Add((long)forLoopStart);
                    bc[forExit + 1] = (long)bc.Count;
                    break;

                case ForGenericStmt fg:
                    foreach (var it in fg.Iterators)
                        CompileExpr(it, bc);
                    bc.Add(OpL("CALL")); bc.Add((long)fg.Iterators.Count);
                    foreach (var vn in fg.VarNames)
                        _locals.Add(vn);
                    // Simplified: push iterated values as locals
                    for (var i = 0; i < fg.VarNames.Count; i++)
                    {
                        bc.Add(OpL("SETL")); bc.Add((long)i);
                    }
                    CompileBlock(fg.Body, bc);
                    break;

                case DoStmt d:
                    CompileBlock(d.Body, bc);
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
                                bc.Add(OpL("SETL")); bc.Add(0L);
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
                    bc.Add(l.Value!);
                    break;

                case LiteralExpr l when l.Kind == LiteralExpr.LiteralKind.String:
                    bc.Add(OpL("LOADK"));
                    bc.Add(l.Value!);
                    break;

                case VarExpr v:
                    bc.Add(OpL("GETG"));
                    bc.Add(v.Name);
                    break;

                case BinaryExpr b:
                    if (b.Op is BinaryOp.And or BinaryOp.Or)
                    {
                        CompileExpr(b.Left, bc);
                        var dupPatch = bc.Count;
                        bc.Add(OpL("JNZ")); bc.Add(0L);
                        if (b.Op == BinaryOp.And) { bc.Add(OpL("LOADK")); bc.Add(0L); }
                        CompileExpr(b.Right, bc);
                        bc[dupPatch + 1] = (long)bc.Count;
                        bc.Add(OpL("ADD"));
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
                        bc.Add(OpL("GETG"));
                        bc.Add(fv.Name);
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

                default:
                    throw new NotSupportedException($"Unsupported expression: {expr?.GetType().Name}");
            }
        }
    }
}