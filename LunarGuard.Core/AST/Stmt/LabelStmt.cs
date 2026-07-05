namespace LunarGuard.Core.AST.Stmt;

public class LabelStmt : Statement
{
    public string Name { get; set; } = "";

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
