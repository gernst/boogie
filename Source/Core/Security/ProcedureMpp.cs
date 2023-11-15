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
    var _minorizer = new MinorizeVisitor(inParams.Concat(outParams).ToDictionary(t => t.Item1.Name, t => t));
    foreach (var req in proc.Requires)
    {
      req.Condition = Util.SolveExpr(req.Condition, new IdentifierExpr(Token.NoToken, Util.MajorP),
        new IdentifierExpr(Token.NoToken, Util.MinorP), _minorizer);
    }
    foreach (var ens in proc.Ensures)
    {
      ens.Condition = Util.SolveExpr(ens.Condition, new IdentifierExpr(Token.NoToken, Util.MajorP),
        new IdentifierExpr(Token.NoToken, Util.MinorP), _minorizer);
    }
  }
}