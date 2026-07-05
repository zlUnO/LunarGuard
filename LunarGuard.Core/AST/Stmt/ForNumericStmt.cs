namespace LunarGuard.Core.AST.Stmt;

public class ForNumericStmt : Statement
{
    public string VarName { get; set; } = "";
    public Expression Start { get; set; } = null!;
    public Expression End { get; set; } = null!;
    public Expression? Step { get; set; }
    public BlockStmt Body { get; set; } = new();

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
