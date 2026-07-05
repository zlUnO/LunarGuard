namespace LunarGuard.Core.AST.Stmt;

public class LocalVarStmt : Statement
{
    public List<string> Names { get; } = new();
    public List<Expression> Values { get; } = new();

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
