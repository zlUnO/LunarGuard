namespace LunarGuard.Core.AST.Stmt;

public class FunctionDeclStmt : Statement
{
    public List<string>? NamePrefix { get; set; }  // e.g., ["a", "b"] for "function a.b()"
    public string? Name { get; set; }               // e.g., "foo" for "function foo()" or nil for anonymous
    public bool IsLocal { get; set; }
    public bool IsMethod { get; set; }              // colon syntax
    public Expr.FuncDeclExpr FuncExpr { get; set; } = null!;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
