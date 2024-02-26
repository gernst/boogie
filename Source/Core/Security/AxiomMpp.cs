using System.Collections.Generic;

namespace Microsoft.Boogie {

  public class AxiomMpp {
    public static Axiom CalculateAxiomMpp(Program program, Axiom axiom, Dictionary<string, (Variable, Variable)> globalVariableDict) {
      var minorizer = new MinorizeVisitor(globalVariableDict);
      var relationalAxiom = new Axiom(axiom.tok, RelationalDuplicator.SolveExpr(program, axiom.Expr, minorizer));
      // remove relational Expressions from original Axioms
      var relationalRemover = new RelationalRemover();
      relationalRemover.VisitAxiom(axiom);

      return relationalAxiom;
    }
  }
}