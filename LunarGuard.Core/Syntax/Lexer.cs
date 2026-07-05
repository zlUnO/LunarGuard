using System.Globalization;
using System.Text;

namespace LunarGuard.Core.Syntax;

public class Lexer
{
    private readonly string _source;
    private int _start;
    private int _current;
    private int _line;
    private int _col;

    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        ["and"] = TokenType.And,
        ["break"] = TokenType.Break,
        ["do"] = TokenType.Do,
        ["else"] = TokenType.Else,
        ["elseif"] = TokenType.ElseIf,
        ["end"] = TokenType.End,
        ["false"] = TokenType.False,
        ["for"] = TokenType.For,
        ["function"] = TokenType.Function,
        ["if"] = TokenType.If,
        ["in"] = TokenType.In,
        ["local"] = TokenType.Local,
        ["nil"] = TokenType.Nil,
        ["not"] = TokenType.Not,
        ["or"] = TokenType.Or,
        ["repeat"] = TokenType.Repeat,
        ["return"] = TokenType.Return,
        ["then"] = TokenType.Then,
        ["true"] = TokenType.True,
        ["until"] = TokenType.Until,
        ["while"] = TokenType.While,
        ["goto"] = TokenType.Goto,
    };

    public Lexer(string source)
    {
        _source = source;
        _line = 1;
        _col = 1;
    }

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        while (!IsAtEnd())
        {
            _start = _current;
            var t = NextToken();
            if (t.Type != TokenType.Comment)
                tokens.Add(t);
        }
        tokens.Add(new Token(TokenType.Eof, "", null, _line, _col));
        return tokens;
    }

    private Token NextToken()
    {
        SkipWhitespace();
        _start = _current;

        if (IsAtEnd()) return Make(TokenType.Eof);

        var c = Advance();

        if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_')
            return IdentifierOrKeyword();
        if (c is >= '0' and <= '9')
            return Number();

        switch (c)
        {
            case '"': case '\'': return StringLiteral(c);
            case '+': return Make(TokenType.Plus);
            case '-':
                if (Match('-'))
                {
                    if (Match('[')) return LongComment();
                    while (Peek() != '\n' && !IsAtEnd()) Advance();
                    return Make(TokenType.Comment);
                }
                return Make(TokenType.Minus);
            case '*': return Make(TokenType.Star);
            case '/': return Make(TokenType.Slash);
            case '%': return Make(TokenType.Percent);
            case '^': return Make(TokenType.Caret);
            case '#': return Make(TokenType.Hash);
            case '(': return Make(TokenType.LParen);
            case ')': return Make(TokenType.RParen);
            case '{': return Make(TokenType.LBrace);
            case '}': return Make(TokenType.RBrace);
            case '[': return Make(TokenType.LBracket);
            case ']': return Make(TokenType.RBracket);
            case ';': return Make(TokenType.Semicolon);
            case ':': return Make(TokenType.Colon);
            case ',': return Make(TokenType.Comma);
            case '=':
                if (Match('=')) return Make(TokenType.Eq);
                return Make(TokenType.Assign);
            case '<':
                if (Match('=')) return Make(TokenType.Leq);
                return Make(TokenType.Lt);
            case '>':
                if (Match('=')) return Make(TokenType.Geq);
                return Make(TokenType.Gt);
            case '~':
                if (Match('=')) return Make(TokenType.Neq);
                return Error("expected '=' after '~'");
            case '.':
                if (Match('.'))
                {
                    if (Match('.')) return Make(TokenType.DotDotDot);
                    return Make(TokenType.DotDot);
                }
                return Make(TokenType.Dot);
        }

        return Error($"unexpected character '{c}'");
    }

    private Token LongComment()
    {
        var level = 0;
        while (Peek() == '=') { Advance(); level++; }
        if (Peek() != '[') { /* not long comment, consume rest of line */ while (Peek() != '\n' && !IsAtEnd()) Advance(); return Make(TokenType.Comment); }
        Advance();
        while (!IsAtEnd())
        {
            if (Advance() == ']')
            {
                var closeLevel = 0;
                while (Peek() == '=') { Advance(); closeLevel++; }
                if (closeLevel == level && Peek() == ']') { Advance(); break; }
            }
        }
        return Make(TokenType.Comment);
    }

    private Token IdentifierOrKeyword()
    {
        while (char.IsLetterOrDigit(Peek()) || Peek() == '_') Advance();
        var text = _source[_start.._current];
        if (Keywords.TryGetValue(text, out var type))
            return Make(type);
        return Make(TokenType.Identifier);
    }

    private Token Number()
    {
        if (_source[_start] == '0' && (Peek() == 'x' || Peek() == 'X'))
        {
            Advance();
            while (IsHexDigit(Peek())) Advance();
        }
        else
        {
            while (char.IsDigit(Peek())) Advance();
            if (Peek() == '.' && char.IsDigit(PeekNext()))
            {
                Advance();
                while (char.IsDigit(Peek())) Advance();
            }
            if (Peek() is 'e' or 'E')
            {
                Advance();
                if (Peek() is '+' or '-') Advance();
                while (char.IsDigit(Peek())) Advance();
            }
        }
        var numStr = _source[_start.._current];
        var numVal = numStr.Contains('.') || numStr.Contains('e') || numStr.Contains('E')
            ? double.Parse(numStr, CultureInfo.InvariantCulture)
            : long.Parse(numStr, CultureInfo.InvariantCulture);
        return Make(TokenType.Number, numVal);
    }

    private Token StringLiteral(char quote)
    {
        var sb = new StringBuilder();
        while (Peek() != quote && !IsAtEnd())
        {
            if (Peek() == '\n') { _line++; }
            if (Peek() == '\\')
            {
                Advance();
                switch (Advance())
                {
                    case 'a': sb.Append('\a'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'v': sb.Append('\v'); break;
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    case '\'': sb.Append('\''); break;
                    case '\n': _line++; break;
                    case 'x':
                        var hex = "" + (char)Advance() + (char)Advance();
                        sb.Append((char)int.Parse(hex, NumberStyles.HexNumber));
                        break;
                    case var d when d is >= '0' and <= '9':
                        var dec = "" + d;
                        if (Peek() is >= '0' and <= '9') dec += (char)Advance();
                        if (Peek() is >= '0' and <= '9') dec += (char)Advance();
                        sb.Append((char)int.Parse(dec));
                        break;
                    default:
                        sb.Append('\\');
                        break;
                }
            }
            else
            {
                sb.Append(Advance());
            }
        }
        Advance();
        return Make(TokenType.String, sb.ToString());
    }

    private static bool IsHexDigit(char c) =>
        c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private Token Make(TokenType type, object? literal = null)
    {
        var lexeme = _source[_start.._current];
        return new Token(type, lexeme, literal, _line, _col - (_current - _start));
    }

    private Token Error(string msg) =>
        new(TokenType.Invalid, msg, null, _line, _col);

    private bool Match(char expected)
    {
        if (IsAtEnd() || _source[_current] != expected) return false;
        _current++;
        _col++;
        return true;
    }

    private char Advance() { _col++; return _source[_current++]; }
    private char Peek() => IsAtEnd() ? '\0' : _source[_current];
    private char PeekNext() => _current + 1 >= _source.Length ? '\0' : _source[_current + 1];
    private bool IsAtEnd() => _current >= _source.Length;

    private void SkipWhitespace()
    {
        while (!IsAtEnd())
        {
            var c = _source[_current];
            switch (c)
            {
                case ' ' or '\t' or '\r': _current++; _col++; break;
                case '\n': _current++; _line++; _col = 1; break;
                case '-':
                    if (_current + 1 < _source.Length && _source[_current + 1] == '-')
                    {
                        _current += 2; _col += 2;
                        if (_current < _source.Length && _source[_current] == '[')
                        {
                            var level = 0;
                            _current++; _col++;
                            while (_current < _source.Length && _source[_current] == '=') { _current++; _col++; level++; }
                            if (_current < _source.Length && _source[_current] == '[') { _current++; _col++; }
                            while (_current < _source.Length)
                            {
                                if (_source[_current] == ']')
                                {
                                    var endLevel = 0; _current++; _col++;
                                    while (_current < _source.Length && _source[_current] == '=') { _current++; _col++; endLevel++; }
                                    if (endLevel == level && _current < _source.Length && _source[_current] == ']') { _current++; _col++; break; }
                                }
                                if (_source[_current] == '\n') { _line++; _col = 1; }
                                _current++; _col++;
                            }
                        }
                        else
                        {
                            while (_current < _source.Length && _source[_current] != '\n') { _current++; _col++; }
                        }
                    }
                    else return;
                    break;
                default: return;
            }
        }
    }
}
