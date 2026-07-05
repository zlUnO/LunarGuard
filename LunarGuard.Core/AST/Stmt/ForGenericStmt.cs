namespace LunarGuard.Core.AST.Stmt;

public class ForGenericStmt : Statement
{
    public List<string> VarNames { get; } = new();
    public List<Expression> Iterators { get; } = new();
    public BlockStmt Body { get; set; } = new();

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
