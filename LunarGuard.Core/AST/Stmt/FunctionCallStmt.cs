namespace LunarGuard.Core.AST.Stmt;

public class FunctionCallStmt : Statement
{
    public Expr.FunctionCallExpr Call { get; set; } = null!;

    public FunctionCallStmt() { }
    public FunctionCallStmt(Expr.FunctionCallExpr call) => Call = call;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
