namespace LunarGuard.Core.AST.Stmt;

public class GotoStmt : Statement
{
    public string LabelName { get; set; } = "";

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
