namespace LunarGuard.Core.Syntax;

public enum TokenType
{
    // Special
    Eof, Invalid, Comment,

    // Keywords
    And, Break, Do, Else, ElseIf, End, False, For, Function,
    If, In, Local, Nil, Not, Or, Repeat, Return, Then, True,
    Until, While, Goto,

    // Literals
    Identifier, Number, String,

    // Operators
    Plus, Minus, Star, Slash, Percent, Caret, Hash,
    Eq, Neq, Lt, Gt, Leq, Geq,
    Assign,
    Dot, Comma, Colon, Semicolon, LParen, RParen,
    LBrace, RBrace, LBracket, RBracket,
    DotDot, DotDotDot,

    // Varargs
    Varargs,
}
