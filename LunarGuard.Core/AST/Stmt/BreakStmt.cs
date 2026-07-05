namespace LunarGuard.Core.AST.Stmt;

public class BreakStmt : Statement
{
    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
