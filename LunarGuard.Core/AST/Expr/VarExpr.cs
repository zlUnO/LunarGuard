namespace LunarGuard.Core.AST.Expr;

public class VarExpr : Expression
{
    public string Name { get; set; }

    public VarExpr(string name) => Name = name;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
}
