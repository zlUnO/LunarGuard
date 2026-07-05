namespace LunarGuard.Core.AST.Expr;

public class IndexExpr : Expression
{
    public Expression Object { get; }
    public Expression Index { get; }

    public IndexExpr(Expression obj, Expression index)
    {
        Object = obj;
        Index = index;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
