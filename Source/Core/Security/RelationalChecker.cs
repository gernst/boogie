using Microsoft.Boogie;

namespace Core;

public class RelationalChecker : ReadOnlyVisitor
{

  public static bool IsRelational(Absy absy)
  {
    var checker = new RelationalChecker();
    checker.Visit(absy);
    return checker.isRelational;
  }

  private bool isRelational;

  private RelationalChecker()
  {
    isRelational = false;
  }

  public override Expr VisitLowExpr(LowExpr node)
  {
    isRelational = true;
    return base.VisitLowExpr(node);
  }

  public override Expr VisitLowEventExpr(LowEventExpr node)
  {
    isRelational = true;
    return base.VisitLowEventExpr(node);
  }
}