using LunarGuard.Core.AST;
using LunarGuard.Core.AST.Stmt;
using LunarGuard.Core.AST.Expr;

namespace LunarGuard.Core.Syntax;

public class Parser
{
    private readonly List<Token> _tokens;
    private int _current;

    public Parser(List<Token> tokens) => _tokens = tokens;

    public List<Statement> Parse()
    {
        var stmts = new List<Statement>();
        var safety = 0;
        while (!Check(TokenType.Eof) && !IsAtEnd())
        {
            if (safety++ > 10000) break;
            stmts.AddRange(ParseStatement());
        }
        return stmts;
    }

    private List<Statement> ParseStatement()
    {
        if (Match(TokenType.Semicolon)) return new List<Statement>();

        if (Match(TokenType.If)) return new List<Statement> { ParseIf() };
        if (Match(TokenType.While)) return new List<Statement> { ParseWhile() };
        if (Match(TokenType.Repeat)) return new List<Statement> { ParseRepeat() };
        if (Match(TokenType.For)) return new List<Statement> { ParseFor() };
        if (Match(TokenType.Do)) return new List<Statement> { ParseDo() };
        if (Match(TokenType.Return)) return new List<Statement> { ParseReturn() };
        if (Match(TokenType.Break)) return new List<Statement> { new BreakStmt { Line = Previous().Line } };
        if (Match(TokenType.Goto)) return new List<Statement> { ParseGoto() };
        if (Match(TokenType.DotDot)) return new List<Statement> { ParseLabel() };

        if (Match(TokenType.Local))
        {
            if (Match(TokenType.Function))
                return new List<Statement> { ParseLocalFunctionDecl() };
            return new List<Statement> { ParseLocalVar() };
        }

        if (Match(TokenType.Function))
            return new List<Statement> { ParseFunctionDecl() };

        if (Match(TokenType.DotDotDot))
        {
            var label = Previous();
            if (Match(TokenType.DotDot)) return new List<Statement> { ParseLabelAfter(label) };
            else if (_current > 0) { _current--; }
        }

        return ParsePrefixStatement();
    }

    private List<Statement> ParsePrefixStatement()
    {
        var save = _current;
        var expr = ParsePrefixExpr();

        if (Match(TokenType.Assign) || Match(TokenType.Comma))
        {
            _current = save;
            return new List<Statement> { ParseAssignment() };
        }

        if (expr is FunctionCallExpr callExpr)
            return new List<Statement> { new FunctionCallStmt(callExpr) };

        _current = save;
        return new List<Statement> { ParseAssignment() };
    }

    private IfStmt ParseIf()
    {
        var stmt = new IfStmt { Line = Previous().Line };
        stmt.Branches.Add((ParseExpression(), ParseBlockAfterThen()));
        while (Match(TokenType.ElseIf))
            stmt.Branches.Add((ParseExpression(), ParseBlockAfterThen()));
        if (Match(TokenType.Else))
            stmt.ElseBody = ParseBlock();
        Consume(TokenType.End, "expected 'end' after if");
        return stmt;
    }

    private BlockStmt ParseBlockAfterThen()
    {
        Consume(TokenType.Then, "expected 'then' after condition");
        return ParseBlock();
    }

    private WhileStmt ParseWhile()
    {
        var stmt = new WhileStmt { Line = Previous().Line };
        stmt.Condition = ParseExpression();
        Consume(TokenType.Do, "expected 'do' after while condition");
        stmt.Body = ParseBlock();
        Consume(TokenType.End, "expected 'end' after while");
        return stmt;
    }

    private RepeatStmt ParseRepeat()
    {
        var stmt = new RepeatStmt { Line = Previous().Line };
        stmt.Body = ParseBlock();
        Consume(TokenType.Until, "expected 'until' after repeat");
        stmt.Condition = ParseExpression();
        return stmt;
    }

    private Statement ParseFor()
    {
        var name = Consume(TokenType.Identifier, "expected variable name after 'for'");
        if (Match(TokenType.Assign))
        {
            var stmt = new ForNumericStmt { VarName = name.Lexeme, Line = name.Line };
            stmt.Start = ParseExpression();
            Consume(TokenType.Comma, "expected ',' after for start");
            stmt.End = ParseExpression();
            if (Match(TokenType.Comma))
                stmt.Step = ParseExpression();
            Consume(TokenType.Do, "expected 'do' after for range");
            stmt.Body = ParseBlock();
            Consume(TokenType.End, "expected 'end' after for");
            return stmt;
        }
        else
        {
            var stmt = new ForGenericStmt { Line = name.Line };
            stmt.VarNames.Add(name.Lexeme);
            while (Match(TokenType.Comma))
                stmt.VarNames.Add(Consume(TokenType.Identifier, "expected variable name").Lexeme);
            Consume(TokenType.In, "expected 'in' after for variables");
            stmt.Iterators.Add(ParseExpression());
            while (Match(TokenType.Comma))
                stmt.Iterators.Add(ParseExpression());
            Consume(TokenType.Do, "expected 'do' after for iterators");
            stmt.Body = ParseBlock();
            Consume(TokenType.End, "expected 'end' after for");
            return stmt;
        }
    }

    private DoStmt ParseDo()
    {
        var stmt = new DoStmt { Line = Previous().Line };
        stmt.Body = ParseBlock();
        Consume(TokenType.End, "expected 'end' after do");
        return stmt;
    }

    private ReturnStmt ParseReturn()
    {
        var stmt = new ReturnStmt { Line = Previous().Line };
        if (!Check(TokenType.End) && !Check(TokenType.ElseIf) && !Check(TokenType.Else) && !Check(TokenType.Until) && !Check(TokenType.Eof))
        {
            stmt.Values.Add(ParseExpression());
            while (Match(TokenType.Comma))
                stmt.Values.Add(ParseExpression());
        }
        return stmt;
    }

    private GotoStmt ParseGoto()
    {
        var label = Consume(TokenType.Identifier, "expected label name after goto");
        return new GotoStmt { LabelName = label.Lexeme, Line = label.Line };
    }

    private LabelStmt ParseLabel()
    {
        var label = Consume(TokenType.Identifier, "expected label name");
        Consume(TokenType.DotDot, "expected '::' after label");
        return new LabelStmt { Name = label.Lexeme, Line = label.Line };
    }

    private LabelStmt ParseLabelAfter(Token firstToken)
    {
        var label = Consume(TokenType.Identifier, "expected label name");
        Consume(TokenType.DotDot, "expected '::' after label");
        return new LabelStmt { Name = label.Lexeme, Line = firstToken.Line };
    }

    private LocalVarStmt ParseLocalVar()
    {
        var stmt = new LocalVarStmt { Line = Previous().Line };
        stmt.Names.Add(Consume(TokenType.Identifier, "expected variable name after local").Lexeme);
        while (Match(TokenType.Comma))
            stmt.Names.Add(Consume(TokenType.Identifier, "expected variable name").Lexeme);
        if (Match(TokenType.Assign))
        {
            stmt.Values.Add(ParseExpression());
            while (Match(TokenType.Comma))
                stmt.Values.Add(ParseExpression());
        }
        return stmt;
    }

    private FunctionDeclStmt ParseFunctionDecl()
    {
        var stmt = new FunctionDeclStmt { Line = Previous().Line };
        var firstName = Consume(TokenType.Identifier, "expected function name");
        if (Match(TokenType.Dot))
        {
            stmt.NamePrefix = new List<string> { firstName.Lexeme };
            stmt.NamePrefix.Add(Consume(TokenType.Identifier, "expected function name after '.'").Lexeme);
            while (Match(TokenType.Dot))
                stmt.NamePrefix.Add(Consume(TokenType.Identifier, "expected function name after '.'").Lexeme);
            if (Match(TokenType.Colon))
            {
                stmt.IsMethod = true;
                stmt.Name = Consume(TokenType.Identifier, "expected method name").Lexeme;
            }
        }
    else if (Match(TokenType.Colon))
    {
        stmt.NamePrefix = new List<string> { firstName.Lexeme };
        stmt.IsMethod = true;
        stmt.Name = Consume(TokenType.Identifier, "expected method name").Lexeme;
    }
        else
        {
            stmt.Name = firstName.Lexeme;
        }
        stmt.FuncExpr = ParseFuncBody();
        return stmt;
    }

    private FunctionDeclStmt ParseLocalFunctionDecl()
    {
        var name = Consume(TokenType.Identifier, "expected function name");
        return new FunctionDeclStmt
        {
            Name = name.Lexeme,
            IsLocal = true,
            FuncExpr = ParseFuncBody(),
            Line = name.Line,
        };
    }

    private FuncDeclExpr ParseFuncBody()
    {
        var expr = new FuncDeclExpr();
        Consume(TokenType.LParen, "expected '(' after function name");
        if (!Check(TokenType.RParen))
        {
            if (Match(TokenType.DotDotDot))
            {
                expr.IsVararg = true;
            }
            else
            {
                expr.Parameters.Add(Consume(TokenType.Identifier, "expected parameter name").Lexeme);
                while (Match(TokenType.Comma))
                {
                    if (Match(TokenType.DotDotDot))
                    {
                        expr.IsVararg = true;
                        break;
                    }
                    expr.Parameters.Add(Consume(TokenType.Identifier, "expected parameter name").Lexeme);
                }
            }
        }
        Consume(TokenType.RParen, "expected ')' after parameters");
        expr.Body = ParseBlock();
        Consume(TokenType.End, "expected 'end' after function body");
        return expr;
    }

    private AssignmentStmt ParseAssignment()
    {
        var stmt = new AssignmentStmt();
        stmt.Targets.Add(ParsePrefixExpr());
        while (Match(TokenType.Comma))
            stmt.Targets.Add(ParsePrefixExpr());
        if (Match(TokenType.Assign))
        {
            stmt.Values.Add(ParseExpression());
            while (Match(TokenType.Comma))
                stmt.Values.Add(ParseExpression());
        }
        return stmt;
    }

    private Expression ParsePrefixExpr()
    {
        var expr = ParsePrimaryExpr();

        while (true)
        {
            if (Match(TokenType.Dot))
            {
                var name = Consume(TokenType.Identifier, "expected field name after '.'");
                expr = new MemberExpr(expr, name.Lexeme)
                {
                    Line = Previous().Line, Column = Previous().Column
                };
            }
            else if (Match(TokenType.LBracket))
            {
                var index = ParseExpression();
                Consume(TokenType.RBracket, "expected ']' after index");
                expr = new IndexExpr(expr, index)
                {
                    Line = Previous().Line, Column = Previous().Column
                };
            }
            else if (Match(TokenType.Colon))
            {
                var methodName = Consume(TokenType.Identifier, "expected method name after ':'");
                var call = new FunctionCallExpr(expr)
                {
                    IsMethodCall = true,
                    MethodName = methodName.Lexeme,
                    Line = Previous().Line, Column = Previous().Column
                };
                ParseFunctionCallArgs(call);
                expr = call;
            }
            else if (Check(TokenType.LParen) || Check(TokenType.LBrace) || Check(TokenType.String))
            {
                var call = new FunctionCallExpr(expr)
                {
                    Line = Previous().Line, Column = Previous().Column
                };
                ParseFunctionCallArgs(call);
                expr = call;
            }
            else
            {
                break;
            }
        }

        return expr;
    }

    private Expression ParsePrimaryExpr()
    {
        if (Match(TokenType.Nil)) return new LiteralExpr(LiteralExpr.LiteralKind.Nil, null) { Line = Previous().Line };
        if (Match(TokenType.True)) return new LiteralExpr(LiteralExpr.LiteralKind.Boolean, true) { Line = Previous().Line };
        if (Match(TokenType.False)) return new LiteralExpr(LiteralExpr.LiteralKind.Boolean, false) { Line = Previous().Line };
        if (Match(TokenType.Number)) return new LiteralExpr(LiteralExpr.LiteralKind.Number, Previous().Literal) { Line = Previous().Line };
        if (Match(TokenType.String)) return new LiteralExpr(LiteralExpr.LiteralKind.String, Previous().Literal) { Line = Previous().Line };
        if (Match(TokenType.DotDotDot)) return new VarargsExpr() { Line = Previous().Line };
        if (Match(TokenType.Function)) return ParseFunctionLiteral();
        if (Match(TokenType.LBrace)) return ParseTableConstructor();
        if (Match(TokenType.Identifier)) return new VarExpr(Previous().Lexeme) { Line = Previous().Line, Column = Previous().Column };
        if (Match(TokenType.LParen))
        {
            var expr = ParseExpression();
            Consume(TokenType.RParen, "expected ')' after expression");
            return expr;
        }

        throw new ParseException(Peek(), $"unexpected token '{Peek().Lexeme}'");
    }

    private FuncDeclExpr ParseFunctionLiteral()
    {
        return ParseFuncBody();
    }

    private TableConstructorExpr ParseTableConstructor()
    {
        var expr = new TableConstructorExpr();
        if (Check(TokenType.RBrace)) { Advance(); return expr; }

        while (true)
        {
            if (Check(TokenType.RBrace)) break;

            var field = new TableField();

            if (Match(TokenType.LBracket))
            {
                field.Key = ParseExpression();
                Consume(TokenType.RBracket, "expected ']' after key");
                Consume(TokenType.Assign, "expected '=' after key");
            }
            else if (Check(TokenType.Identifier) && CheckNext(TokenType.Assign))
            {
                var name = Advance();
                Advance(); // skip =
                field.Key = new LiteralExpr(LiteralExpr.LiteralKind.String, name.Lexeme);
            }
            else
            {
                field.Value = ParseExpression();
                if (!Check(TokenType.RBrace) && !Check(TokenType.Comma))
                {
                    if (Match(TokenType.Semicolon)) continue;
                    expr.Fields.Add(field);
                    continue;
                }
            }

            if (field.Key != null)
                field.Value = ParseExpression();

            expr.Fields.Add(field);

            if (Match(TokenType.Comma) || Match(TokenType.Semicolon))
                continue;
            break;
        }

        Consume(TokenType.RBrace, "expected '}' after table constructor");
        return expr;
    }

    private void ParseFunctionCallArgs(FunctionCallExpr call)
    {
        if (Match(TokenType.LParen))
        {
            if (!Check(TokenType.RParen))
            {
                call.Arguments.Add(ParseExpression());
                while (Match(TokenType.Comma))
                    call.Arguments.Add(ParseExpression());
            }
            Consume(TokenType.RParen, "expected ')' after arguments");
        }
        else if (Match(TokenType.LBrace))
        {
            if (_current > 0) _current--;
            call.Arguments.Add(ParseTableConstructor());
        }
        else if (Match(TokenType.String))
        {
            call.Arguments.Add(new LiteralExpr(LiteralExpr.LiteralKind.String, Previous().Literal));
        }
    }

    // Expression parsing (precedence climbing)
    private Expression ParseExpression()
    {
        return ParseBinaryExpr(0);
    }

    private readonly (TokenType Type, int Precedence, bool RightAssoc)[] _operators =
    {
        (TokenType.Or, 1, false),
        (TokenType.And, 2, false),
        (TokenType.Lt, 3, false), (TokenType.Gt, 3, false),
        (TokenType.Leq, 3, false), (TokenType.Geq, 3, false),
        (TokenType.Eq, 3, false), (TokenType.Neq, 3, false),
        (TokenType.DotDot, 4, false),
        (TokenType.Plus, 5, false), (TokenType.Minus, 5, false),
        (TokenType.Star, 6, false), (TokenType.Slash, 6, false), (TokenType.Percent, 6, false),
        (TokenType.Hash, 6, false),
        (TokenType.Caret, 7, true),
    };

    private const int MaxUnaryDepth = 64;

    private Expression ParseBinaryExpr(int minPrec, int unaryDepth = 0)
    {
        if (unaryDepth > MaxUnaryDepth)
            throw new ParseException(Peek(), "max unary operator depth exceeded");
        Expression left;
        if (Match(TokenType.Not) || Match(TokenType.Minus) || Match(TokenType.Hash))
        {
            var op = Previous().Type switch
            {
                TokenType.Not => UnaryOp.Not,
                TokenType.Minus => UnaryOp.Negate,
                TokenType.Hash => UnaryOp.Length,
                _ => throw new InvalidOperationException()
            };
            var operand = ParseBinaryExpr(8, unaryDepth + 1);
            left = new UnaryExpr(op, operand) { Line = Previous().Line };
        }
        else
        {
            left = ParsePrefixExpr();
        }

        while (true)
        {
            var found = false;
            BinaryOp? binOp = null;
            var prec = 0;
            var rightAssoc = false;

            for (var i = 0; i < _operators.Length; i++)
            {
                var (type, p, ra) = _operators[i];
                if (Check(type))
                {
                    binOp = type switch
                    {
                        TokenType.Plus => BinaryOp.Add,
                        TokenType.Minus => BinaryOp.Subtract,
                        TokenType.Star => BinaryOp.Multiply,
                        TokenType.Slash => BinaryOp.Divide,
                        TokenType.Percent => BinaryOp.Modulo,
                        TokenType.Caret => BinaryOp.Power,
                        TokenType.DotDot => BinaryOp.Concat,
                        TokenType.Eq => BinaryOp.Eq,
                        TokenType.Neq => BinaryOp.Neq,
                        TokenType.Lt => BinaryOp.Lt,
                        TokenType.Gt => BinaryOp.Gt,
                        TokenType.Leq => BinaryOp.Leq,
                        TokenType.Geq => BinaryOp.Geq,
                        TokenType.And => BinaryOp.And,
                        TokenType.Or => BinaryOp.Or,
                        _ => (BinaryOp?)null
                    };
                    prec = p;
                    rightAssoc = ra;
                    found = true;
                    break;
                }
            }

            if (!found || prec < minPrec || (prec == minPrec && !rightAssoc))
                break;

            Advance();
            var right = ParseBinaryExpr(prec + (rightAssoc ? 0 : 1));
            left = new BinaryExpr(binOp!.Value, left, right) { Line = Previous().Line };
        }

        return left;
    }

    private bool CheckNext(TokenType type)
    {
        if (_current + 1 >= _tokens.Count) return false;
        return _tokens[_current + 1].Type == type;
    }

    private BlockStmt ParseBlock()
    {
        var block = new BlockStmt();
        while (!Check(TokenType.End) && !Check(TokenType.ElseIf) &&
               !Check(TokenType.Else) && !Check(TokenType.Until) &&
               !Check(TokenType.Eof))
        {
            block.Statements.AddRange(ParseStatement());
        }
        return block;
    }

    private bool Match(TokenType type)
    {
        if (!Check(type)) return false;
        Advance();
        return true;
    }

    private bool Check(TokenType type) => !IsAtEnd() && Peek().Type == type;

    private Token Advance()
    {
        if (!IsAtEnd()) _current++;
        return Previous();
    }

    private Token Peek() => _tokens[_current];
    private Token Previous() => _tokens[_current - 1];
    private bool IsAtEnd() => Peek().Type == TokenType.Eof;

    private Token Consume(TokenType type, string message)
    {
        if (Check(type)) return Advance();
        throw new ParseException(Peek(), message);
    }
}

public class ParseException : Exception
{
    public Token Token { get; }

    public ParseException(Token token, string message)
        : base($"({token.Line},{token.Column}): {message} near '{token.Lexeme}'")
    {
        Token = token;
    }
}
