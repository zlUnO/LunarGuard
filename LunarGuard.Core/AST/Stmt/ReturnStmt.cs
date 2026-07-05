namespace LunarGuard.Core.AST.Stmt;

public class ReturnStmt : Statement
{
    public List<Expression> Values { get; } = new();

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
