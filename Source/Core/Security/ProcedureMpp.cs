using System.Collections.Generic;
using System.Linq;
using Microsoft.Boogie;
using Util = Core.Security.Util;

namespace Core;

public  static class ProcedureMpp
{
  
  public static void CalculateProcedureMpp(Procedure proc)
  {
    var inParams = Util.CalculateInParams(proc.InParams);
    var outParams = Util.DuplicateVariables(proc.OutParams);
    proc.InParams = Util.FlattenVarList(inParams);
    proc.OutParams = Util.FlattenVarList(outParams);
    proc.Modifies = proc.Modifies.SelectMany(idExpr =>
    {
      var minorModifies = new IdentifierExpr(idExpr.tok, Util.MinorPrefix + idExpr.Name, idExpr.Immutable);
      return new List<IdentifierExpr> { idExpr, minorModifies };
    }).ToList();
    var minorizer = new MinorizeVisitor(inParams.Concat(outParams).ToDictionary(t => t.Item1.Name, t => t));
    foreach (var req in proc.Requires)
    {
      req.Condition = Util.SolveExpr(req.Condition, new IdentifierExpr(Token.NoToken, Util.MajorP),
        new IdentifierExpr(Token.NoToken, Util.MinorP), minorizer);
    }
    foreach (var ens in proc.Ensures)
    {
      ens.Condition = Util.SolveExpr(ens.Condition, new IdentifierExpr(Token.NoToken, Util.MajorP),
        new IdentifierExpr(Token.NoToken, Util.MinorP), minorizer);
    }
  }
}