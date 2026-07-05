namespace LunarGuard.Core.AST.Expr;

public enum BinaryOp
{
    Add, Subtract, Multiply, Divide, Modulo, Power,
    Concat,
    Eq, Neq, Lt, Gt, Leq, Geq,
    And, Or,
}

public class BinaryExpr : Expression
{
    public BinaryOp Op { get; }
    public Expression Left { get; }
    public Expression Right { get; }

    public BinaryExpr(BinaryOp op, Expression left, Expression right)
    {
        Op = op;
        Left = left;
        Right = right;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
