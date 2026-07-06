using System.Globalization;
using System.Text;
using LunarGuard.Core.AST;
using LunarGuard.Core.AST.Stmt;
using LunarGuard.Core.AST.Expr;

namespace LunarGuard.Core.CodeGen;

public class LuaWriter : IAstVisitor
{
    private readonly StringBuilder _sb = new();
    private int _indent;
    private bool _needsNewline = true;

    public string Write(AstNode node)
    {
        _sb.Clear();
        _indent = 0;
        _needsNewline = true;
        node.Accept(this);
        return _sb.ToString();
    }

    public string Write(IEnumerable<Statement> stmts)
    {
        _sb.Clear();
        _indent = 0;
        _needsNewline = true;
        foreach (var stmt in stmts)
            stmt.Accept(this);
        return _sb.ToString();
    }

    private void WriteIndent()
    {
        if (_needsNewline)
        {
            _sb.Append(new string(' ', _indent * 2));
            _needsNewline = false;
        }
    }

    private void Write(string s)
    {
        WriteIndent();
        _sb.Append(s);
    }

    private void WriteLine(string s = "")
    {
        if (s.Length > 0)
            Write(s);
        _sb.AppendLine();
        _needsNewline = true;
    }

    private void WriteComma() => _sb.Append(", ");

    public void Visit(BlockStmt node)
    {
        foreach (var stmt in node.Statements)
            stmt.Accept(this);
    }

    public void Visit(AssignmentStmt node)
    {
        WriteIndent();
        for (var i = 0; i < node.Targets.Count; i++)
        {
            if (i > 0) _sb.Append(", ");
            node.Targets[i].Accept(this);
        }
        _sb.Append(" = ");
        for (var i = 0; i < node.Values.Count; i++)
        {
            if (i > 0) _sb.Append(", ");
            node.Values[i].Accept(this);
        }
        WriteLine();
    }

    public void Visit(LocalVarStmt node)
    {
        Write("local ");
        for (var i = 0; i < node.Names.Count; i++)
        {
            if (i > 0) _sb.Append(", ");
            _sb.Append(node.Names[i]);
        }
        if (node.Values.Count > 0)
        {
            _sb.Append(" = ");
            for (var i = 0; i < node.Values.Count; i++)
            {
                if (i > 0) _sb.Append(", ");
                node.Values[i].Accept(this);
            }
        }
        WriteLine();
    }

    public void Visit(IfStmt node)
    {
        for (var i = 0; i < node.Branches.Count; i++)
        {
            Write(i == 0 ? "if " : "elseif ");
            node.Branches[i].Condition.Accept(this);
            _sb.Append(" then");
            WriteLine();
            _indent++;
            node.Branches[i].Body.Accept(this);
            _indent--;
        }
        if (node.ElseBody != null)
        {
            WriteLine("else");
            _indent++;
            node.ElseBody.Accept(this);
            _indent--;
        }
        WriteLine("end");
    }

    public void Visit(WhileStmt node)
    {
        Write("while ");
        node.Condition.Accept(this);
        _sb.Append(" do");
        WriteLine();
        _indent++;
        node.Body.Accept(this);
        _indent--;
        WriteLine("end");
    }

    public void Visit(RepeatStmt node)
    {
        WriteLine("repeat");
        _indent++;
        node.Body.Accept(this);
        _indent--;
        Write("until ");
        node.Condition.Accept(this);
        WriteLine();
    }

    public void Visit(ForNumericStmt node)
    {
        Write($"for {node.VarName} = ");
        node.Start.Accept(this);
        _sb.Append(", ");
        node.End.Accept(this);
        if (node.Step != null)
        {
            _sb.Append(", ");
            node.Step.Accept(this);
        }
        _sb.Append(" do");
        WriteLine();
        _indent++;
        node.Body.Accept(this);
        _indent--;
        WriteLine("end");
    }

    public void Visit(ForGenericStmt node)
    {
        Write("for ");
        for (var i = 0; i < node.VarNames.Count; i++)
        {
            if (i > 0) _sb.Append(", ");
            _sb.Append(node.VarNames[i]);
        }
        _sb.Append(" in ");
        for (var i = 0; i < node.Iterators.Count; i++)
        {
            if (i > 0) _sb.Append(", ");
            node.Iterators[i].Accept(this);
        }
        _sb.Append(" do");
        WriteLine();
        _indent++;
        node.Body.Accept(this);
        _indent--;
        WriteLine("end");
    }

    public void Visit(FunctionCallStmt node)
    {
        WriteIndent();
        node.Call.Accept(this);
        WriteLine();
    }

    public void Visit(DoStmt node)
    {
        WriteLine("do");
        _indent++;
        node.Body.Accept(this);
        _indent--;
        WriteLine("end");
    }

    public void Visit(ReturnStmt node)
    {
        Write("return");
        if (node.Values.Count > 0)
        {
            _sb.Append(" ");
            for (var i = 0; i < node.Values.Count; i++)
            {
                if (i > 0) _sb.Append(", ");
                node.Values[i].Accept(this);
            }
        }
        WriteLine();
    }

    public void Visit(BreakStmt node)
    {
        WriteLine("break");
    }

    public void Visit(GotoStmt node)
    {
        WriteLine($"goto {node.LabelName}");
    }

    public void Visit(LabelStmt node)
    {
        WriteLine($"::{node.Name}::");
    }

    public void Visit(FunctionDeclStmt node)
    {
        if (node.IsLocal) Write("local ");
        Write("function");
        if (node.NamePrefix != null)
        {
            _sb.Append(" ");
            _sb.Append(string.Join(".", node.NamePrefix));
            if (node.IsMethod && node.Name != null)
                _sb.Append($":{node.Name}");
        }
        else if (node.Name != null)
        {
            _sb.Append($" {node.Name}");
        }
        _sb.Append('(');
        for (var i = 0; i < node.FuncExpr.Parameters.Count; i++)
        {
            if (i > 0) _sb.Append(", ");
            _sb.Append(node.FuncExpr.Parameters[i]);
        }
        if (node.FuncExpr.IsVararg)
        {
            if (node.FuncExpr.Parameters.Count > 0) _sb.Append(", ");
            _sb.Append("...");
        }
        _sb.Append(')');
        WriteLine();
        _indent++;
        node.FuncExpr.Body.Accept(this);
        _indent--;
        WriteLine("end");
    }

    // Expressions
    public void Visit(LiteralExpr node)
    {
        switch (node.Kind)
        {
            case LiteralExpr.LiteralKind.Nil:
                _sb.Append("nil");
                break;
            case LiteralExpr.LiteralKind.Boolean:
                _sb.Append((bool)node.Value! ? "true" : "false");
                break;
            case LiteralExpr.LiteralKind.Number:
                _sb.Append(Convert.ToString(node.Value, CultureInfo.InvariantCulture));
                break;
            case LiteralExpr.LiteralKind.String:
                _sb.Append(EscapeString((string)node.Value!));
                break;
        }
    }

    private static string EscapeString(string s)
    {
        var sb = new StringBuilder("\"");
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsSurrogate(c))
            {
                var cp = char.ConvertToUtf32(s, i);
                sb.Append($"\\u{{{cp:x}}}");
                i++;
                continue;
            }
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\0': sb.Append("\\0"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    public void Visit(VarExpr node) => _sb.Append(node.Name);
    public void Visit(VarargsExpr node) => _sb.Append("...");

    private static string OpToString(BinaryOp op) => op switch
    {
        BinaryOp.Add => " + ", BinaryOp.Subtract => " - ",
        BinaryOp.Multiply => " * ", BinaryOp.Divide => " / ",
        BinaryOp.Modulo => " % ", BinaryOp.Power => " ^ ",
        BinaryOp.Concat => " .. ",
        BinaryOp.Eq => " == ", BinaryOp.Neq => " ~= ",
        BinaryOp.Lt => " < ", BinaryOp.Gt => " > ",
        BinaryOp.Leq => " <= ", BinaryOp.Geq => " >= ",
        BinaryOp.And => " and ", BinaryOp.Or => " or ",
        _ => throw new ArgumentOutOfRangeException(nameof(op))
    };

    public void Visit(BinaryExpr node)
    {
        _sb.Append('(');
        node.Left.Accept(this);
        _sb.Append(OpToString(node.Op));
        node.Right.Accept(this);
        _sb.Append(')');
    }

    private static string UnaryToString(UnaryOp op) => op switch
    {
        UnaryOp.Negate => "-", UnaryOp.Not => "not ", UnaryOp.Length => "#",
        _ => throw new ArgumentOutOfRangeException(nameof(op))
    };

    public void Visit(UnaryExpr node)
    {
        _sb.Append(UnaryToString(node.Op));
        if (node.Op == UnaryOp.Negate || node.Op == UnaryOp.Length) { }
        else _sb.Append(' ');
        node.Operand.Accept(this);
    }

    public void Visit(FunctionCallExpr node)
    {
        var isAnonFunc = node.Callee is FuncDeclExpr;
        if (isAnonFunc) _sb.Append('(');
        node.Callee.Accept(this);
        if (isAnonFunc) _sb.Append(')');
        if (node.IsMethodCall && node.MethodName != null)
            _sb.Append($":{node.MethodName}");
        if (node.Arguments.Count == 1 && node.Arguments[0] is LiteralExpr { Kind: LiteralExpr.LiteralKind.String })
        {
            _sb.Append(' ');
            node.Arguments[0].Accept(this);
        }
        else
        {
            _sb.Append('(');
            for (var i = 0; i < node.Arguments.Count; i++)
            {
                if (i > 0) _sb.Append(", ");
                node.Arguments[i].Accept(this);
            }
            _sb.Append(')');
        }
    }

    public void Visit(TableConstructorExpr node)
    {
        _sb.Append('{');
        if (node.Fields.Count > 0)
        {
            _sb.Append(' ');
            for (var i = 0; i < node.Fields.Count; i++)
            {
                if (i > 0) _sb.Append(", ");
                var f = node.Fields[i];
                if (f.Key != null)
                {
                    _sb.Append('[');
                    f.Key.Accept(this);
                    _sb.Append("] = ");
                }
                f.Value.Accept(this);
            }
            _sb.Append(' ');
        }
        _sb.Append('}');
    }

    public void Visit(IndexExpr node)
    {
        node.Object.Accept(this);
        _sb.Append('[');
        node.Index.Accept(this);
        _sb.Append(']');
    }

    public void Visit(MemberExpr node)
    {
        node.Object.Accept(this);
        _sb.Append($".{node.Member}");
    }

    public void Visit(FuncDeclExpr node)
    {
        _sb.Append("function(");
        for (var i = 0; i < node.Parameters.Count; i++)
        {
            if (i > 0) _sb.Append(", ");
            _sb.Append(node.Parameters[i]);
        }
        if (node.IsVararg)
        {
            if (node.Parameters.Count > 0) _sb.Append(", ");
            _sb.Append("...");
        }
        _sb.Append(')');
        WriteLine();
        _indent++;
        node.Body.Accept(this);
        _indent--;
        Write("end");
    }

    public void Visit(ConcatExpr node)
    {
        for (var i = 0; i < node.Parts.Count; i++)
        {
            if (i > 0) _sb.Append(" .. ");
            node.Parts[i].Accept(this);
        }
    }
}
