using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Boogie {

  // This package implements Modular Product Programs [Eilers, Müller, Hitz, 2018]

  // The translation essentially duplicates all variables,
  // to account for potential "minor" executions over which an attacker reasons.

  // It adds two basic expressions:
  // - low(e) denotes that expression e is of low sensitivity, semantically e = minor(e)
  // - lowEvent denotes that the control flows can be merged at a particular program point

  // Functions with boolean return value and annotated with {: relational } are
  // considered to be relational predicates, all other functions are unchanged.

  // TODO:
  // - Currently we enforce the strong property of constant-time execution, which enables lockstep reasoning about branching.
  //   This restriction will be lifted eventually once it is clear how to treat unstructured control flow modularly.
  // - Integration with the Dafny command line tool is implemented, but not with the language server

  // Contributed by: Maximilian Doods and Gidon Ernst <gidon.ernst@lmu.de>
  public static class Security {
    public static void CalculateMpp(Program program, List<string> exclusions = null) {
      exclusions ??= new List<string> { "well-formedness", "well_formedness"};

      var duplicatedMutGlobals = program.GlobalVariables
        .Where(glob => glob.IsMutable)
        .Select(glob => {
          var minorGlob = new GlobalVariable(glob.tok,
            new TypedIdent(glob.TypedIdent.tok, RelationalDuplicator.MinorPrefix + glob.Name, glob.TypedIdent.Type));
          var minorizer = new MinorizeVisitor(new Dictionary<string, (Variable, Variable)>
            { { glob.Name, (glob, minorGlob) } });
          if (glob.TypedIdent.WhereExpr != null) {
            minorGlob.TypedIdent.WhereExpr = minorizer.VisitExpr(glob.TypedIdent.WhereExpr);
          }
          return (glob, minorGlob);
        })
        .ToList();

      var globalVariableDict = program.GlobalVariables
        .Where(glob => !glob.IsMutable)
        .Concat(program.Constants.Cast<Variable>())
        .Select(glob => (glob, glob))
        .Concat(duplicatedMutGlobals.Select(t => ((Variable)t.glob, (Variable)t.minorGlob)))
        .ToDictionary(t => t.Item1.Name, t => t);

      var emptyDict = new Dictionary<string, (Variable, Variable)>();
        
      program.AddTopLevelDeclarations(duplicatedMutGlobals.Select(x => x.minorGlob));

      var relationalFunctions = program.Functions
        .Where(f => RelationalChecker.IsRelationalFunction(f))
        .Select(f => FunctionMpp.CalculateFunctionMpp(program, f, emptyDict))
        .ToList();
      program.AddTopLevelDeclarations(relationalFunctions);

      var relationalAxioms = program.Axioms
        .Where(a => RelationalChecker.IsRelational(program, a))
        .Select(a => AxiomMpp.CalculateAxiomMpp(program, a, emptyDict))
        .ToList();
      program.AddTopLevelDeclarations(relationalAxioms);

      var newImplementations = program.Implementations
        .Where(i => !RelationalChecker.IsExcludedRelationalProcedure(i, exclusions))
        .Select(i => new ImplementationMpp(program, i, globalVariableDict, exclusions).Implementation).ToList();

      program.RemoveTopLevelDeclarations(i => i is Implementation && !RelationalChecker.IsExcludedRelationalProcedure(i, exclusions));
      program.AddTopLevelDeclarations(newImplementations);

      program.Procedures
        .Where(p => !RelationalChecker.IsExcludedRelationalProcedure(p, exclusions))
        .ForEach(p => ProcedureMpp.CalculateProcedureMpp(program, p, globalVariableDict));

      var relationalRemover = new RelationalRemover();
      program.TopLevelDeclarations
        .Where(d => RelationalChecker.IsExcludedRelationalProcedure(d, exclusions))
        .ForEach(d => relationalRemover.Visit(d));
    }
  }
}