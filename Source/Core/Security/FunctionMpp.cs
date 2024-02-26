using System.Collections.Generic;

namespace Microsoft.Boogie {
  public class FunctionMpp {
    public static Function CalculateFunctionMpp(Program program, Function function, Dictionary<string, (Variable, Variable)> globalVariableDict) {
      var minorizer = new MinorizeVisitor(globalVariableDict);
      var inParams = RelationalDuplicator.CalculateInParams(function.InParams, minorizer);
      return new Function(function.tok, function.Name + RelationalDuplicator.RelationalSuffix, RelationalDuplicator.FlattenVarList(inParams),
        function.OutParams[0]);
    }
  }
}