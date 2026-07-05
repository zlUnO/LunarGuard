namespace LunarGuard.Core.AST;

public abstract class AstNode
{
    public int Line { get; set; }
    public int Column { get; set; }

    public abstract void Accept(IAstVisitor visitor);
}

public interface IAstVisitor
{
    void Visit(Stmt.BlockStmt node);
    void Visit(Stmt.AssignmentStmt node);
    void Visit(Stmt.LocalVarStmt node);
    void Visit(Stmt.IfStmt node);
    void Visit(Stmt.WhileStmt node);
    void Visit(Stmt.RepeatStmt node);
    void Visit(Stmt.ForNumericStmt node);
    void Visit(Stmt.ForGenericStmt node);
    void Visit(Stmt.FunctionCallStmt node);
    void Visit(Stmt.DoStmt node);
    void Visit(Stmt.ReturnStmt node);
    void Visit(Stmt.BreakStmt node);
    void Visit(Stmt.GotoStmt node);
    void Visit(Stmt.LabelStmt node);
    void Visit(Stmt.FunctionDeclStmt node);
    void Visit(Expr.LiteralExpr node);
    void Visit(Expr.VarExpr node);
    void Visit(Expr.BinaryExpr node);
    void Visit(Expr.UnaryExpr node);
    void Visit(Expr.FunctionCallExpr node);
    void Visit(Expr.TableConstructorExpr node);
    void Visit(Expr.IndexExpr node);
    void Visit(Expr.MemberExpr node);
    void Visit(Expr.FuncDeclExpr node);
    void Visit(Expr.VarargsExpr node);
    void Visit(Expr.ConcatExpr node);
}

public abstract class Statement : AstNode { }
public abstract class Expression : AstNode { }
