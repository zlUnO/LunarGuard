namespace LunarGuard.Core.AST.Expr;

public class TableField
{
    public Expression? Key { get; set; }
    public Expression Value { get; set; } = null!;
}

public class TableConstructorExpr : Expression
{
    public List<TableField> Fields { get; } = new();

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
