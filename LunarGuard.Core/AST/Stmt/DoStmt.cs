namespace LunarGuard.Core.AST.Stmt;

public class DoStmt : Statement
{
    public BlockStmt Body { get; set; } = new();

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
