namespace LunarGuard.Core.AST.Expr;

public class FuncDeclExpr : Expression
{
    public List<string> Parameters { get; } = new();
    public bool IsVararg { get; set; }
    public Stmt.BlockStmt Body { get; set; } = new();

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
