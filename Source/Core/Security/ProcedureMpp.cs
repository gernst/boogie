using System.Collections.Generic;
using System.Linq;
using Microsoft.Boogie;
using Util = Core.Security.Util;

namespace Core;

public  static class ProcedureMpp
{
  
  public static void CalculateProcedureMpp(Program program, Procedure proc, Dictionary<string, (Variable, Variable)> globalVariableDict)
  {
    var minorizer = new MinorizeVisitor(globalVariableDict);
    var inParams = Util.CalculateInParams(proc.InParams, minorizer);
    var outParams = Util.DuplicateVariables(proc.OutParams, minorizer);
    proc.InParams = Util.FlattenVarList(inParams);
    proc.OutParams = Util.FlattenVarList(outParams);
    proc.Modifies = proc.Modifies.SelectMany(idExpr =>
    {
      var minorModifies = new IdentifierExpr(idExpr.tok, Util.MinorPrefix + idExpr.Name, idExpr.Immutable);
      return new List<IdentifierExpr> { idExpr, minorModifies };
    }).ToList();
    minorizer = minorizer.AddTemporaryVariables(inParams.Concat(outParams).ToList());
    foreach (var req in proc.Requires)
    {
      req.Condition = Util.SolveExpr(program, req.Condition, minorizer);
    }
    foreach (var ens in proc.Ensures)
    {
      ens.Condition = Util.SolveExpr(program, ens.Condition, minorizer);
    }
  }
}