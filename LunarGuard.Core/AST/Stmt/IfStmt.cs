namespace LunarGuard.Core.AST.Stmt;

public class IfStmt : Statement
{
    public List<(Expression Condition, BlockStmt Body)> Branches { get; } = new();
    public BlockStmt? ElseBody { get; set; }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
