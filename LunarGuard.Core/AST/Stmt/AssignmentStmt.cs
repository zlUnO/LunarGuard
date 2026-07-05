namespace LunarGuard.Core.AST.Stmt;

public class AssignmentStmt : Statement
{
    public List<Expression> Targets { get; } = new();
    public List<Expression> Values { get; } = new();

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
