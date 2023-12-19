using Microsoft.Boogie;

namespace Core.Security;

public class RelationalRemover : StandardVisitor
{
  public override Expr VisitLowExpr(LowExpr node)
  {
    return Expr.True;
  }

  public override Expr VisitLowEventExpr(LowEventExpr node)
  {
    return Expr.True;
  }

  public override Implementation VisitImplementation(Implementation node)
  {
    this.VisitBlockList(node.Blocks);
    return node;
  }
}