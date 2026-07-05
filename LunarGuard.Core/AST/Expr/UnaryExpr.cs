namespace LunarGuard.Core.AST.Expr;

public enum UnaryOp { Negate, Not, Length, Minus }

public class UnaryExpr : Expression
{
    public UnaryOp Op { get; }
    public Expression Operand { get; }

    public UnaryExpr(UnaryOp op, Expression operand)
    {
        Op = op;
        Operand = operand;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
