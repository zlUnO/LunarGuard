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

        var rng = new Random();
        var vmPrefix = $"vm_{rng.Next():x8}";
        var vmm = new VmGenerator(vmPrefix, rng);

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

        root.Statements.Insert(0, CreateVMInterpreter(vmPrefix));
    }

    private static Statement CreateVMInterpreter(string prefix)
    {
        var codeName = $"{prefix}_code";
        var stackName = $"{prefix}_stk";

        var vmSrc = $@"
local {codeName} = ...
local {stackName} = {{}}
local pc = 1
local sp = 0
local regs = {{}}

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

while pc <= #{codeName} do
    local op = {codeName}[pc]
    pc = pc + 1

    if op == 0 then -- nop
    elseif op == 1 then -- push
        push({codeName}[pc]); pc = pc + 1
    elseif op == 2 then -- pop
        pop()
    elseif op == 3 then -- add
        local a = pop(); local b = pop(); push(a + b)
    elseif op == 4 then -- sub
        local a = pop(); local b = pop(); push(b - a)
    elseif op == 5 then -- mul
        local a = pop(); local b = pop(); push(a * b)
    elseif op == 6 then -- div
        local a = pop(); local b = pop(); push(b / a)
    elseif op == 7 then -- mod
        local a = pop(); local b = pop(); push(b % a)
    elseif op == 8 then -- pow
        local a = pop(); local b = pop(); push(b ^ a)
    elseif op == 9 then -- concat
        local a = pop(); local b = pop(); push(b .. a)
    elseif op == 10 then -- eq
        local a = pop(); local b = pop(); push(a == b)
    elseif op == 11 then -- lt
        local a = pop(); local b = pop(); push(a < b)
    elseif op == 12 then -- gt
        local a = pop(); local b = pop(); push(a > b)
    elseif op == 13 then -- jmp
        pc = {codeName}[pc]
    elseif op == 14 then -- jz
        local target = {codeName}[pc]; pc = pc + 1
        if not pop() then pc = target end
    elseif op == 15 then -- jnz
        local target = {codeName}[pc]; pc = pc + 1
        if pop() then pc = target end
    elseif op == 16 then -- call
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
    elseif op == 17 then -- ret
        break
    elseif op == 18 then -- loadk
        push({codeName}[pc]); pc = pc + 1
    elseif op == 19 then -- loadn
        push(nil)
    elseif op == 20 then -- loadb
        if {codeName}[pc] == 1 then push(true) else push(false) end; pc = pc + 1
    elseif op == 21 then -- loadnil
        push(nil)
    elseif op == 22 then -- setg
        local name = {codeName}[pc]; pc = pc + 1
        _G[name] = pop()
    elseif op == 23 then -- getg
        local name = {codeName}[pc]; pc = pc + 1
        push(_G[name])
    elseif op == 24 then -- setl
        local idx = {codeName}[pc]; pc = pc + 1
        regs[idx] = pop()
    elseif op == 25 then -- getl
        local idx = {codeName}[pc]; pc = pc + 1
        push(regs[idx])
    elseif op == 26 then -- newtable
        push({{}})
    elseif op == 27 then -- settable
        local val = pop(); local key = pop(); local tbl = pop()
        tbl[key] = val
        push(tbl)
    elseif op == 28 then -- gettable
        local key = pop(); local tbl = pop()
        push(tbl[key])
    elseif op == 29 then -- neq
        local a = pop(); local b = pop(); push(a ~= b)
    elseif op == 30 then -- leq
        local a = pop(); local b = pop(); push(a <= b)
    elseif op == 31 then -- geq
        local a = pop(); local b = pop(); push(a >= b)
    elseif op == 32 then -- len
        push(#pop())
    elseif op == 33 then -- neg
        push(-pop())
    elseif op == 34 then -- not
        push(not pop())
    elseif op == 35 then -- append
        local val = pop(); local tbl = pop()
        tbl[#tbl + 1] = val
        push(tbl)
    elseif op == 36 then -- inc
        local idx = {codeName}[pc]; pc = pc + 1
        regs[idx] = regs[idx] + 1
    end
end";

        return new LocalVarStmt
        {
            Names = { $"{prefix}_run" },
            Values =
            {
                new FunctionCallExpr(new VarExpr("load"))
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
        private readonly Random _rng;
        private readonly HashSet<string> _locals = new();

        public VmGenerator(string prefix, Random rng)
        {
            _prefix = prefix;
            _rng = rng;
        }

        public Statement VirtualizeFunction(FunctionDeclStmt funcDecl)
        {
            var bytecode = new List<object>();
            CompileBlock(funcDecl.FuncExpr.Body, bytecode);

            var bytecodeTable = new TableConstructorExpr();
            foreach (var instr in bytecode)
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
            var funcName = funcDecl.Name ?? $"vm_{_rng.Next():x8}";
            // load("return vm_run(code)", "=name")({bytecode...})
            var vmCall = new FunctionCallExpr(
                new FunctionCallExpr(new VarExpr("load"))
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

            // Method: table.method = load(...)({...})
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

            // Standalone: local funcName = load(...)({...})
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
                            bc.Add(19L); // push nil for missing values
                    }
                    var assignCount = Math.Min(l.Names.Count, Math.Max(l.Values.Count, 1));
                    for (var i = 0; i < assignCount; i++)
                    {
                        bc.Add(24L); bc.Add((long)i);
                    }
                    if (l.Values.Count > l.Names.Count)
                    {
                        var extra = l.Values.Count - l.Names.Count;
                        for (var i = 0; i < extra; i++)
                            bc.Add(2L); // pop extra values
                    }
                    foreach (var name in l.Names)
                        _locals.Add(name);
                    break;
                case ReturnStmt r:
                    foreach (var v in r.Values)
                        CompileExpr(v, bc);
                    bc.Add(17L);
                    break;
                case FunctionCallStmt fc:
                    CompileExpr(fc.Call, bc);
                    bc.Add(2L);
                    break;
                case IfStmt i:
                    var branchEndPatches = new List<int>();
                    for (var j = 0; j < i.Branches.Count; j++)
                    {
                        var (cond, body) = i.Branches[j];
                        CompileExpr(cond, bc);
                        var jmpzPatch = bc.Count;
                        bc.Add(14L); bc.Add(0L);
                        CompileBlock(body, bc);
                        if (j < i.Branches.Count - 1 || i.ElseBody != null)
                        {
                            branchEndPatches.Add(bc.Count);
                            bc.Add(13L); bc.Add(0L);
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
                    bc.Add(14L); bc.Add(0L);
                    CompileBlock(w.Body, bc);
                    bc.Add(13L); bc.Add((long)loopStart);
                    bc[exitPatch + 1] = (long)bc.Count;
                    break;
                case ForNumericStmt fn:
                    _locals.Add(fn.VarName);
                    CompileExpr(fn.Start, bc);
                    bc.Add(24L); bc.Add(0L); // reg 0 = loop var
                    CompileExpr(fn.End, bc);
                    bc.Add(24L); bc.Add(1L); // reg 1 = bound
                    if (fn.Step != null)
                    {
                        CompileExpr(fn.Step, bc);
                        bc.Add(24L); bc.Add(2L); // reg 2 = step
                    }
                    var forLoopStart = bc.Count;
                    bc.Add(25L); bc.Add(0L);
                    bc.Add(25L); bc.Add(1L);
                    bc.Add(30L); // leq: push(reg1 <= reg0) 
                    var forExit = bc.Count;
                    bc.Add(14L); bc.Add(0L);
                    CompileBlock(fn.Body, bc);
                    bc.Add(36L); bc.Add(0L); // inc reg 0
                    bc.Add(13L); bc.Add((long)forLoopStart);
                    bc[forExit + 1] = (long)bc.Count;
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
                            bc.Add(18L);
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
                                bc.Add(24L); bc.Add(0L);
                            }
                            else
                            {
                                bc.Add(22L);
                                bc.Add(ve.Name);
                            }
                        }
                        else
                        {
                            bc.Add(27L);
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
                    bc.Add(21L);
                    break;
                case LiteralExpr l when l.Kind == LiteralExpr.LiteralKind.Boolean:
                    bc.Add(20L);
                    bc.Add((bool)l.Value! ? 1L : 0L);
                    break;
                case LiteralExpr l when l.Kind == LiteralExpr.LiteralKind.Number:
                    bc.Add(18L);
                    bc.Add(l.Value!);
                    break;
                case LiteralExpr l when l.Kind == LiteralExpr.LiteralKind.String:
                    bc.Add(18L);
                    bc.Add(l.Value!);
                    break;
                case VarExpr v:
                    bc.Add(23L);
                    bc.Add(v.Name);
                    break;
                case BinaryExpr b:
                    if (b.Op is BinaryOp.And or BinaryOp.Or)
                    {
                        var shortLabel = bc.Count;
                        bc.Add(18L); bc.Add(0L);
                        CompileExpr(b.Left, bc);
                        var dupPatch = bc.Count;
                        bc.Add(15L); bc.Add(0L);
                        if (b.Op == BinaryOp.And) { bc.Add(18L); bc.Add(0L); }
                        CompileExpr(b.Right, bc);
                        bc[dupPatch + 1] = (long)bc.Count;
                        bc.Add(3L);
                        bc[shortLabel + 1] = (long)bc.Count;
                    }
                    else
                    {
                        CompileExpr(b.Left, bc);
                        CompileExpr(b.Right, bc);
                        bc.Add(b.Op switch
                        {
                            BinaryOp.Add => 3L, BinaryOp.Subtract => 4L,
                            BinaryOp.Multiply => 5L, BinaryOp.Divide => 6L,
                            BinaryOp.Modulo => 7L, BinaryOp.Power => 8L,
                            BinaryOp.Concat => 9L,
                            BinaryOp.Eq => 10L, BinaryOp.Neq => 29L,
                            BinaryOp.Lt => 11L, BinaryOp.Gt => 12L,
                            BinaryOp.Leq => 30L, BinaryOp.Geq => 31L,
                            _ => throw new NotSupportedException()
                        });
                    }
                    break;
                case UnaryExpr u:
                    if (u.Op == UnaryOp.Length)
                    { CompileExpr(u.Operand, bc); bc.Add(32L); }
                    else if (u.Op == UnaryOp.Negate)
                    { CompileExpr(u.Operand, bc); bc.Add(33L); }
                    else // Not
                    {
                        CompileExpr(u.Operand, bc);
                        bc.Add(34L);
                    }
                    break;
                case MemberExpr m:
                    CompileExpr(m.Object, bc);
                    bc.Add(28L);
                    bc.Add(m.Member);
                    break;
                case TableConstructorExpr t:
                    bc.Add(26L);
                    foreach (var f in t.Fields)
                    {
                        if (f.Key != null)
                        {
                            CompileExpr(f.Key, bc);
                            CompileExpr(f.Value, bc);
                            bc.Add(27L);
                        }
                        else
                        {
                            CompileExpr(f.Value, bc);
                            bc.Add(35L);
                        }
                    }
                    break;
                case FunctionCallExpr fc:
                    if (fc.Callee is VarExpr fv)
                    {
                        bc.Add(23L);
                        bc.Add(fv.Name);
                    }
                    else if (fc.Callee is MemberExpr me)
                    {
                        CompileExpr(me.Object, bc);
                        bc.Add(28L);
                        bc.Add(me.Member);
                        if (fc.IsMethodCall)
                            CompileExpr(me.Object, bc);
                    }
                    foreach (var a in fc.Arguments)
                        CompileExpr(a, bc);
                    var nargs = fc.Arguments.Count + (fc.IsMethodCall ? 1L : 0L);
                    bc.Add(16L);
                    bc.Add(nargs);
                    break;
                case IndexExpr ix:
                    CompileExpr(ix.Object, bc);
                    CompileExpr(ix.Index, bc);
                    bc.Add(28L);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported expression: {expr?.GetType().Name}");
            }
        }

        private void CompileStatements(IReadOnlyList<Statement> stmts, List<object> bc)
        {
            foreach (var stmt in stmts)
                CompileStatement(stmt, bc);
        }
    }
}
