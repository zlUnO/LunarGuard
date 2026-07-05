namespace LunarGuard.Core.AST.Expr;

public class MemberExpr : Expression
{
    public Expression Object { get; }
    public string Member { get; }

    public MemberExpr(Expression obj, string member)
    {
        Object = obj;
        Member = member;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
