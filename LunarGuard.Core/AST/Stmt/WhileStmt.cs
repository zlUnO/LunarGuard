namespace LunarGuard.Core.AST.Stmt;

public class WhileStmt : Statement
{
    public Expression Condition { get; set; } = null!;
    public BlockStmt Body { get; set; } = new();

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
