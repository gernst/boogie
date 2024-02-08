using System.Collections.Generic;
using System.Linq;
using Microsoft.Boogie;

namespace Core.Security;

public static class Security
{

  public static void CalculateMpp(Program program, List<string> exclusions = null)
  {
    exclusions ??= new List<string>();
    var duplicatedMutGlobals = program.GlobalVariables
      .Where(glob => glob.IsMutable)
      .Select(glob =>
      {
        var minorGlob = new GlobalVariable(glob.tok,
          new TypedIdent(glob.TypedIdent.tok, Util.MinorPrefix + glob.Name, glob.TypedIdent.Type));
        var minorizer = new MinorizeVisitor(new Dictionary<string, (Variable, Variable)>
          { { glob.Name, (glob, minorGlob) } });
        if (glob.TypedIdent.WhereExpr != null)
        {
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

    program.AddTopLevelDeclarations(duplicatedMutGlobals.Select(x => x.minorGlob));

    var newImplementations = program.Implementations
      .Where(i => !RelationalChecker.IsExcludedRelationalProcedure(i, exclusions))
      .Select(i => new ImplementationMpp(program, i, globalVariableDict, exclusions).Implementation).ToList();

    program.RemoveTopLevelDeclarations(i => i is Implementation && !RelationalChecker.IsExcludedRelationalProcedure(i, exclusions));
    program.AddTopLevelDeclarations(newImplementations);

    program.Functions
      .Where(f => RelationalChecker.IsRelationalFunction(f))
      .ForEach(f => FunctionMpp.CalculateFunctionMpp(program, f));

    program.Axioms
      .Where(a => RelationalChecker.IsRelational(program, a))
      .ForEach( a => AxiomMpp.CalculateAxiomMpp(program, a));
      
    program.Procedures
      .Where(p => !RelationalChecker.IsExcludedRelationalProcedure(p, exclusions))
      .ForEach(p => ProcedureMpp.CalculateProcedureMpp(program, p, globalVariableDict));

    var relationalRemover = new RelationalRemover();
    program.TopLevelDeclarations
      .Where(d => RelationalChecker.IsExcludedRelationalProcedure(d, exclusions))
      .ForEach(d => relationalRemover.Visit(d));
  }
}