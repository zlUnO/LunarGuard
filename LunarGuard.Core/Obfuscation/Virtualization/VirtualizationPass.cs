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
        var xorKey = NextHex(rng, 4);
        var vmm = new VmGenerator(vmPrefix, rng, xorKey);

        var statements = root.Statements.ToList();
        var vmFunctions = new List<(Statement Stmt, Statement Original)>();

        foreach (var stmt in statements)
        {
            if (stmt is FunctionDeclStmt fd)
            {
                if (CanVirtualize(fd))
                {
                    var virtualized = vmm.VirtualizeFunction(fd);
                    vmFunctions.Add((virtualized, stmt));
                }
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

    private static bool CanVirtualize(FunctionDeclStmt fd)
    {
        return !HasUnsupportedConstructs(fd.FuncExpr.Body);
    }

    private static bool HasUnsupportedConstructs(BlockStmt block)
    {
        foreach (var stmt in block.Statements)
        {
            switch (stmt)
            {
                case GotoStmt:
                case LabelStmt:
                    return true;
                case FunctionDeclStmt:
                    return true;
                case FunctionCallStmt fc:
                    if (HasUnsupportedExpr(fc.Call))
                        return true;
                    break;
                case IfStmt i:
                    foreach (var (_, b) in i.Branches)
                        if (HasUnsupportedConstructs(b)) return true;
                    if (i.ElseBody != null && HasUnsupportedConstructs(i.ElseBody))
                        return true;
                    break;
                case WhileStmt w:
                    if (HasUnsupportedConstructs(w.Body)) return true;
                    break;
                case RepeatStmt r:
                    if (HasUnsupportedConstructs(r.Body)) return true;
                    break;
                case DoStmt d:
                    if (HasUnsupportedConstructs(d.Body)) return true;
                    break;
                case ForNumericStmt fn:
                    if (HasUnsupportedConstructs(fn.Body)) return true;
                    break;
                case ForGenericStmt fg:
                    if (HasUnsupportedConstructs(fg.Body)) return true;
                    break;
                case ReturnStmt rs:
                    foreach (var v in rs.Values)
                        if (HasUnsupportedExpr(v)) return true;
                    break;
                case LocalVarStmt l:
                    foreach (var v in l.Values)
                        if (HasUnsupportedExpr(v)) return true;
                    break;
                case AssignmentStmt a:
                    foreach (var t in a.Targets)
                        if (HasUnsupportedExpr(t)) return true;
                    foreach (var v in a.Values)
                        if (HasUnsupportedExpr(v)) return true;
                    break;
            }
        }
        return false;
    }

    private static bool HasUnsupportedExpr(Expression expr)
    {
        switch (expr)
        {
            case FuncDeclExpr:
                return true;
            case VarargsExpr:
                return true;
            case FunctionCallExpr fc:
                if (HasUnsupportedExpr(fc.Callee)) return true;
                foreach (var a in fc.Arguments)
                    if (HasUnsupportedExpr(a)) return true;
                break;
            case BinaryExpr b:
                return HasUnsupportedExpr(b.Left) || HasUnsupportedExpr(b.Right);
            case UnaryExpr u:
                return HasUnsupportedExpr(u.Operand);
            case IndexExpr ix:
                return HasUnsupportedExpr(ix.Object) || HasUnsupportedExpr(ix.Index);
            case MemberExpr me:
                return HasUnsupportedExpr(me.Object);
            case TableConstructorExpr tc:
                foreach (var f in tc.Fields)
                {
                    if (f.Key != null && HasUnsupportedExpr(f.Key)) return true;
                    if (HasUnsupportedExpr(f.Value)) return true;
                }
                break;
            case ConcatExpr cc:
                foreach (var p in cc.Parts)
                    if (HasUnsupportedExpr(p)) return true;
                break;
        }
        return false;
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

        var xorKeyInt = Convert.ToInt32(xorKeyHex, 16);

        var opcodes = Enumerable.Range(0, 48).OrderBy(_ => { var b = new byte[1]; rng.GetBytes(b); return b[0]; }).ToList();

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
            ["DUP"] = opcodes[39],
            ["ROT2"] = opcodes[40],
            ["PUSHT"] = opcodes[41],
            ["PUSHF"] = opcodes[42],
            ["SWAP"] = opcodes[43],
            ["SETGLOBAL"] = opcodes[44],
            ["GETGLOBAL"] = opcodes[45],
            ["PCALL"] = opcodes[46],
            ["JMPIF"] = opcodes[47],
        };

        var antiDebugBuf = new byte[8];
        rng.GetBytes(antiDebugBuf);
        var dbgConst1 = BitConverter.ToInt32(antiDebugBuf, 0) & 0x7FFFFFFF;

        var vmSrc = $@"
local {codeName} = ...
local {stackName} = {{}}
local {regsName} = {{}}
local pc = 1
local sp = 0

local {keyName} = {xorKeyInt}
for i = 1, #{codeName} do
    local v = {codeName}[i]
    if type(v) == 'number' and v == math.floor(v) and v >= 0 then
        {codeName}[i] = v + {keyName}
    end
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

local function peek()
    return {stackName}[sp]
end

local handlers = {{}}

handlers[{opNames["PUSH"]}] = function()
    push({codeName}[pc]); pc = pc + 1
end

handlers[{opNames["POP"]}] = function()
    pop()
end

handlers[{opNames["DUP"]}] = function()
    push(peek())
end

handlers[{opNames["ROT2"]}] = function()
    local a = pop(); local b = pop(); push(a); push(b)
end

handlers[{opNames["SWAP"]}] = function()
    local a = pop(); local b = pop(); push(a); push(b)
end

handlers[{opNames["PUSHT"]}] = function()
    push(true)
end

handlers[{opNames["PUSHF"]}] = function()
    push(false)
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

handlers[{opNames["JMPIF"]}] = function()
    local cond = pop(); local target = {codeName}[pc]; pc = pc + 1
    if cond then pc = target end
end

handlers[{opNames["CALL"]}] = function()
    local nargs = {codeName}[pc]; pc = pc + 1
    local func = pop()
    local args = {{}}
    for i = 1, nargs do
        args[i] = pop()
    end
    local ok, results = pcall(table.pack, func(table.unpack(args)))
    if ok then
        for i = #results, 1, -1 do
            push(results[i])
        end
    else
        push(nil)
    end
end

handlers[{opNames["PCALL"]}] = function()
    local nargs = {codeName}[pc]; pc = pc + 1
    local func = pop()
    local args = {{}}
    for i = 1, nargs do
        args[i] = pop()
    end
    local ok, err = pcall(func, table.unpack(args))
    if ok then
        push(true); push(nil)
    else
        push(false); push(err)
    end
end

handlers[{opNames["RET"]}] = function()
    pc = #{codeName} + 1
end

handlers[{opNames["LOADK"]}] = function()
    push({codeName}[pc]); pc = pc + 1
end

handlers[{opNames["LOADNIL"]}] = function()
    push(nil)
end

handlers[{opNames["LOADB"]}] = function()
    if {codeName}[pc] == 1 then push(true) else push(false) end; pc = pc + 1
end

handlers[{opNames["SETG"]}] = function()
    local name = {codeName}[pc]; pc = pc + 1
    _G[name] = pop()
end

handlers[{opNames["GETG"]}] = function()
    local name = {codeName}[pc]; pc = pc + 1
    push(_G[name])
end

handlers[{opNames["SETGLOBAL"]}] = function()
    local name = {codeName}[pc]; pc = pc + 1
    _G[name] = pop()
end

handlers[{opNames["GETGLOBAL"]}] = function()
    local name = {codeName}[pc]; pc = pc + 1
    push(_G[name])
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
    local ok, info = pcall(debug.getinfo, 0)
    if not ok or not info then
        local idx = {codeName}[pc]; pc = pc + 1
        {regsName}[idx] = ({regsName}[idx] or 0) - {dbgConst1}
    end
end

handlers[{opNames["INTEG_CHECK"]}] = function()
    local expected = {codeName}[pc]; pc = pc + 1
    if #{codeName} ~= expected then
        {codeName} = {{}}
    end
end

handlers[{opNames["NOP"]}] = function() end

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
        private readonly Dictionary<string, int> _regMap = new();
        private int _nextReg;
        private readonly Dictionary<string, int> _opMap;
        private readonly int _xorKey;
        private readonly int _nopOp;
        private readonly int _dbgCheckOp;
        private readonly int _integCheckOp;

        public VmGenerator(string prefix, RandomNumberGenerator rng, string xorKeyHex)
        {
            _prefix = prefix;
            _rng = rng;

            var opcodes = Enumerable.Range(0, 48).OrderBy(_ => { var b = new byte[1]; rng.GetBytes(b); return b[0]; }).ToList();
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
                ["DUP"] = opcodes[39],
                ["ROT2"] = opcodes[40],
                ["PUSHT"] = opcodes[41],
                ["PUSHF"] = opcodes[42],
                ["SWAP"] = opcodes[43],
                ["SETGLOBAL"] = opcodes[44],
                ["GETGLOBAL"] = opcodes[45],
                ["PCALL"] = opcodes[46],
                ["JMPIF"] = opcodes[47],
            };
            _nopOp = _opMap["NOP"];
            _dbgCheckOp = _opMap["DBG_CHECK"];
            _integCheckOp = _opMap["INTEG_CHECK"];
            _xorKey = Convert.ToInt32(xorKeyHex, 16);
        }

        private int Op(string name) => _opMap[name];
        private long OpL(string name) => (long)_opMap[name];

        private int Reg(string name)
        {
            if (!_regMap.ContainsKey(name))
            {
                _regMap[name] = _nextReg++;
            }
            return _regMap[name];
        }

        public Statement VirtualizeFunction(FunctionDeclStmt funcDecl)
        {
            _regMap.Clear();
            _locals.Clear();
            _nextReg = 0;

            var bytecode = new List<object>();

            for (var i = 0; i < funcDecl.FuncExpr.Parameters.Count; i++)
            {
                var paramReg = Reg(funcDecl.FuncExpr.Parameters[i]);
                _locals.Add(funcDecl.FuncExpr.Parameters[i]);
            }

            CompileBlock(funcDecl.FuncExpr.Body, bytecode);

            var injectedBytecode = new List<object> { OpL("DBG_CHECK"), 0L };
            injectedBytecode.AddRange(bytecode);
            var expectedLength = injectedBytecode.Count + 1;
            injectedBytecode.Add(OpL("INTEG_CHECK"));
            injectedBytecode.Add((long)expectedLength);

            var nopCount = (byte)(injectedBytecode.Count * 0.05);
            var nopBuf = new byte[1];
            for (var i = 0; i < nopCount; i++)
            {
                _rng.GetBytes(nopBuf);
                var pos = nopBuf[0] % (injectedBytecode.Count + 1);
                injectedBytecode.Insert(pos, OpL("NOP"));
            }

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
            var savedRegs = new Dictionary<string, int>(_regMap);
            var savedLocals = new HashSet<string>(_locals);
            var savedNextReg = _nextReg;

            foreach (var stmt in block.Statements)
                CompileStatement(stmt, bc);

            _nextReg = savedNextReg;
            _locals.Clear();
            foreach (var l in savedLocals)
                _locals.Add(l);
            _regMap.Clear();
            foreach (var kv in savedRegs)
                _regMap[kv.Key] = kv.Value;
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
                        {
                            bc.Add(OpL("LOADNIL"));
                            var nilReg = Reg(l.Names[i]);
                            bc.Add(OpL("SETL")); bc.Add((long)nilReg);
                        }
                    }
                    for (var i = 0; i < l.Names.Count && i < Math.Max(l.Values.Count, 1); i++)
                    {
                        var localReg = Reg(l.Names[i]);
                        bc.Add(OpL("SETL")); bc.Add((long)localReg);
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

                case RepeatStmt r:
                    var rLoopStart = bc.Count;
                    CompileBlock(r.Body, bc);
                    CompileExpr(r.Condition, bc);
                    bc.Add(OpL("JZ")); bc.Add((long)rLoopStart);
                    break;

                case ForNumericStmt fn:
                    var ivReg = Reg(fn.VarName);
                    _locals.Add(fn.VarName);
                    var endReg = _nextReg++;
                    var stepReg = -1;
                    CompileExpr(fn.Start, bc);
                    bc.Add(OpL("SETL")); bc.Add((long)ivReg);
                    CompileExpr(fn.End, bc);
                    bc.Add(OpL("SETL")); bc.Add((long)endReg);
                    if (fn.Step != null)
                    {
                        stepReg = _nextReg++;
                        CompileExpr(fn.Step, bc);
                        bc.Add(OpL("SETL")); bc.Add((long)stepReg);
                    }
                    var forLoopStart = bc.Count;
                    bc.Add(OpL("GETL")); bc.Add((long)ivReg);
                    bc.Add(OpL("GETL")); bc.Add((long)endReg);
                    bc.Add(OpL("LEQ"));
                    var forExit = bc.Count;
                    bc.Add(OpL("JZ")); bc.Add(0L);
                    CompileBlock(fn.Body, bc);
                    if (stepReg >= 0)
                    {
                        bc.Add(OpL("GETL")); bc.Add((long)ivReg);
                        bc.Add(OpL("GETL")); bc.Add((long)stepReg);
                        bc.Add(OpL("ADD"));
                    }
                    else
                    {
                        bc.Add(OpL("GETL")); bc.Add((long)ivReg);
                        bc.Add(OpL("LOADK")); bc.Add(1L);
                        bc.Add(OpL("ADD"));
                    }
                    bc.Add(OpL("SETL")); bc.Add((long)ivReg);
                    bc.Add(OpL("JMP")); bc.Add((long)forLoopStart);
                    bc[forExit + 1] = (long)bc.Count;
                    break;

                case ForGenericStmt fg:
                    var fRegAlloc = _nextReg++;
                    var sRegAlloc = _nextReg++;
                    var varRegAlloc = _nextReg++;

                    for (var i = fg.Iterators.Count - 1; i >= 0; i--)
                        CompileExpr(fg.Iterators[i], bc);

                    bc.Add(OpL("SETL")); bc.Add((long)varRegAlloc);
                    bc.Add(OpL("SETL")); bc.Add((long)sRegAlloc);
                    bc.Add(OpL("SETL")); bc.Add((long)fRegAlloc);

                    var gLoopStart = bc.Count;

                    bc.Add(OpL("GETL")); bc.Add((long)varRegAlloc);
                    bc.Add(OpL("GETL")); bc.Add((long)sRegAlloc);
                    bc.Add(OpL("GETL")); bc.Add((long)fRegAlloc);
                    bc.Add(OpL("CALL")); bc.Add(2L);

                    for (var i = fg.VarNames.Count - 1; i >= 0; i--)
                    {
                        var lr = Reg(fg.VarNames[i]);
                        bc.Add(OpL("SETL")); bc.Add((long)lr);
                    }

                    var firstVarReg = Reg(fg.VarNames[0]);
                    bc.Add(OpL("GETL")); bc.Add((long)firstVarReg);
                    var gExitPatch = bc.Count;
                    bc.Add(OpL("JZ")); bc.Add(0L);

                    foreach (var vn in fg.VarNames)
                        _locals.Add(vn);
                    CompileBlock(fg.Body, bc);

                    bc.Add(OpL("GETL")); bc.Add((long)firstVarReg);
                    bc.Add(OpL("SETL")); bc.Add((long)varRegAlloc);

                    bc.Add(OpL("JMP")); bc.Add((long)gLoopStart);
                    bc[gExitPatch + 1] = (long)bc.Count;
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
                            if (_regMap.ContainsKey(ve.Name))
                            {
                                bc.Add(OpL("SETL")); bc.Add((long)_regMap[ve.Name]);
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
                    if (_regMap.ContainsKey(v.Name))
                    {
                        bc.Add(OpL("GETL")); bc.Add((long)_regMap[v.Name]);
                    }
                    else
                    {
                        bc.Add(OpL("GETG")); bc.Add(v.Name);
                    }
                    break;

                case BinaryExpr b:
                    if (b.Op is BinaryOp.And or BinaryOp.Or)
                    {
                        CompileExpr(b.Left, bc);
                        bc.Add(OpL("DUP"));
                        var dupPatch = bc.Count;
                        bc.Add(OpL("JNZ")); bc.Add(0L);
                        if (b.Op == BinaryOp.And)
                        {
                            bc.Add(OpL("POP"));
                            CompileExpr(b.Right, bc);
                        }
                        else
                        {
                            bc.Add(OpL("POP"));
                            CompileExpr(b.Right, bc);
                        }
                        bc[dupPatch + 1] = (long)bc.Count;
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
                    for (var i = fc.Arguments.Count - 1; i >= 0; i--)
                        CompileExpr(fc.Arguments[i], bc);
                    if (fc.IsMethodCall && fc.Callee is MemberExpr meM)
                    {
                        CompileExpr(meM.Object, bc);
                    }
                    if (fc.Callee is VarExpr fv)
                    {
                        bc.Add(OpL("GETG")); bc.Add(fv.Name);
                    }
                    else if (fc.Callee is MemberExpr me)
                    {
                        CompileExpr(me.Object, bc);
                        bc.Add(OpL("GETTABLE")); bc.Add(me.Member);
                    }
                    var nargs = fc.Arguments.Count + (fc.IsMethodCall ? 1L : 0L);
                    var usePcall = fc.Callee is MemberExpr;
                    bc.Add(OpL(usePcall ? "PCALL" : "CALL"));
                    bc.Add(nargs);
                    break;

                case IndexExpr ix:
                    CompileExpr(ix.Object, bc);
                    CompileExpr(ix.Index, bc);
                    bc.Add(OpL("GETTABLE"));
                    break;

                case ConcatExpr cc:
                    for (var i = 0; i < cc.Parts.Count; i++)
                        CompileExpr(cc.Parts[i], bc);
                    for (var i = 1; i < cc.Parts.Count; i++)
                        bc.Add(OpL("CONCAT"));
                    break;

                default:
                    throw new NotSupportedException($"Unsupported expression: {expr?.GetType().Name}");
            }
        }
    }
}
