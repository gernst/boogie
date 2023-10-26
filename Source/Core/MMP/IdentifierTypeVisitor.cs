using System.Collections.Generic;
using Microsoft.Boogie;

namespace Core;

public class IdentifierTypeVisitor : StandardVisitor
{
  private List<Variable> _variables;
  
  public IdentifierTypeVisitor(List<Variable> variables)
  {
    _variables = variables;
  }
  public override Expr VisitIdentifierExpr(IdentifierExpr node)
  {
    var v = _variables.Find(v => v.Name.Equals(node.Name));
    node.Type = v.TypedIdent.Type;
    return base.VisitIdentifierExpr(node);
  }
}