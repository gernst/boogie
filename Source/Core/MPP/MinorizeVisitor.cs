using System.Collections.Generic;
using System.Linq;
using Microsoft.Boogie;

namespace Core;

public class MinorizeVisitor : Duplicator
{
  private readonly Dictionary<string, (Variable, Variable)> _variables;

  public MinorizeVisitor(Dictionary<string, (Variable, Variable)> allVariables)
  {
    _variables = allVariables;
  }

  public override LocalVariable VisitLocalVariable(LocalVariable node)
  {
    LocalVariable minorVar = (LocalVariable)_variables[node.Name].Item2;
    return minorVar;
  }

  public override Expr VisitIdentifierExpr(IdentifierExpr node)
  {
    IdentifierExpr newIdentifierExpr = new IdentifierExpr(Token.NoToken, _variables[node.Name].Item2);
    return newIdentifierExpr;
  }
}