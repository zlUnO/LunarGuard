namespace LunarGuard.Core.AST.Expr;

public class LiteralExpr : Expression
{
    public enum LiteralKind { Nil, Boolean, Number, String }

    public LiteralKind Kind { get; }
    public object? Value { get; set; }

    public LiteralExpr(LiteralKind kind, object? value)
    {
        Kind = kind;
        Value = value;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
