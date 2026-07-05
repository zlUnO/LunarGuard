namespace LunarGuard.Core.AST.Expr;

public class VarargsExpr : Expression
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
