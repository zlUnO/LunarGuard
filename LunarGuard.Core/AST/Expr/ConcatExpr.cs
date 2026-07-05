namespace LunarGuard.Core.AST.Expr;

public class ConcatExpr : Expression
{
    public List<Expression> Parts { get; } = new();

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
