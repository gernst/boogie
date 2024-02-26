using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Boogie {
  public static class ProcedureMpp {

    public static void CalculateProcedureMpp(Program program, Procedure proc, Dictionary<string, (Variable, Variable)> globalVariableDict) {
      var minorizer = new MinorizeVisitor(globalVariableDict);
      var inParams = RelationalDuplicator.CalculateInParams(proc.InParams, minorizer);
      var outParams = RelationalDuplicator.DuplicateVariables(proc.OutParams, minorizer);
      proc.InParams = RelationalDuplicator.FlattenVarList(inParams);
      proc.OutParams = RelationalDuplicator.FlattenVarList(outParams);
      proc.Modifies = proc.Modifies.SelectMany(idExpr => {
        var minorModifies = new IdentifierExpr(idExpr.tok, RelationalDuplicator.MinorPrefix + idExpr.Name, idExpr.Immutable);
        return new List<IdentifierExpr> { idExpr, minorModifies };
      }).ToList();
      minorizer = minorizer.AddTemporaryVariables(inParams.Concat(outParams).ToList());
      foreach (var req in proc.Requires) {
        req.Condition = RelationalDuplicator.SolveExpr(program, req.Condition, minorizer);
      }
      foreach (var ens in proc.Ensures) {
        ens.Condition = RelationalDuplicator.SolveExpr(program, ens.Condition, minorizer);
      }
    }
  }
}