namespace LunarGuard.Core.AST.Stmt;

public class BlockStmt : Statement
{
    public List<Statement> Statements { get; } = new();

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
