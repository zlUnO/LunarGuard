namespace LunarGuard.Core.AST.Expr;

public class FunctionCallExpr : Expression
{
    public Expression Callee { get; }
    public List<Expression> Arguments { get; } = new();
    public bool IsMethodCall { get; set; }  // colon syntax
    public string? MethodName { get; set; }

    public FunctionCallExpr(Expression callee) => Callee = callee;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
