namespace LunarGuard.Core.Syntax;

public class Token
{
    public TokenType Type { get; }
    public string Lexeme { get; }
    public object? Literal { get; }
    public int Line { get; }
    public int Column { get; }

    public Token(TokenType type, string lexeme, object? literal, int line, int col)
    {
        Type = type;
        Lexeme = lexeme;
        Literal = literal;
        Line = line;
        Column = col;
    }

    public override string ToString() => $"{Type} '{Lexeme}'";
}
