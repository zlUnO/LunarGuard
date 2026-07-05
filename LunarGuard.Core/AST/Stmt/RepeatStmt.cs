namespace LunarGuard.Core.AST.Stmt;

public class RepeatStmt : Statement
{
    public BlockStmt Body { get; set; } = new();
    public Expression Condition { get; set; } = null!;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
