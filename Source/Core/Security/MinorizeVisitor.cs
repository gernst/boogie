using System.Buffers;
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

  public MinorizeVisitor AddTemporaryVariables(List<Variable> tempVars)
  {
    var duplicator = new Duplicator();

    var dupedVariables = tempVars.Select(v =>
    {
      var minorVar = duplicator.VisitVariable(v);
      minorVar.Name = "minor_" + minorVar.Name;
      return (v, minorVar);
    }).ToList();

    return AddTemporaryVariables(dupedVariables);
  }
  public MinorizeVisitor AddTemporaryVariables(List<(Variable, Variable)> tempVars)
  {
    var combinedVars = new Dictionary<string, (Variable, Variable)>();
    _variables.ForEach(pair => combinedVars.Add(pair.Key, pair.Value));
    tempVars.ForEach(tup => combinedVars.Add(tup.Item1.Name, tup));
    return new MinorizeVisitor(combinedVars);
  }

  public override LocalVariable VisitLocalVariable(LocalVariable node)
  {
    var minorVar = (LocalVariable)_variables[node.Name].Item2;
    return minorVar;
  }

  public override BoundVariable VisitBoundVariable(BoundVariable node)
  {
    return new BoundVariable(node.tok, new TypedIdent(node.TypedIdent.tok, "minor_" + node.TypedIdent.Name, node.TypedIdent.Type));
  }

  public override Expr VisitIdentifierExpr(IdentifierExpr node) {
    if (_variables.TryGetValue(node.Name, out var variable)) {
      return new IdentifierExpr(node.tok, variable.Item2);
    }

    return node;
  }

  public override Expr VisitForallExpr(ForallExpr node)
  {
    return new ForallExpr(node.tok, node.Dummies, this.AddTemporaryVariables(node.Dummies.Select(v => (v, v)).ToList()).VisitExpr(node.Body));
  }

  public override Expr VisitExistsExpr(ExistsExpr node)
  {
    return new ExistsExpr(node.tok, node.Dummies, this.AddTemporaryVariables(node.Dummies.Select(v => (v, v)).ToList()).VisitExpr(node.Body));
  }

  public override Expr VisitLambdaExpr(LambdaExpr node)
  {
    return new LambdaExpr(node.tok, node.TypeParameters, node.Dummies, node.Attributes,
      this.AddTemporaryVariables(node.Dummies.Select(v => (v, v)).ToList()).VisitExpr(node.Body));
  }
}