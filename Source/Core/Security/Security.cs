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
      .Where(i => !IsExcluded(i, exclusions))
      .Select(i => new ImplementationMpp(i, globalVariableDict, exclusions).Implementation).ToList(); 
    program.RemoveTopLevelDeclarations(dec => dec is Implementation && !IsExcluded(dec, exclusions));
    program.AddTopLevelDeclarations(newImplementations);

    program.Procedures
      .Where(p => !IsExcluded(p, exclusions))
      .ForEach(p => ProcedureMpp.CalculateProcedureMpp(p, globalVariableDict));

    var relationalRemover = new RelationalRemover();
    program.TopLevelDeclarations
      .Where(d => IsExcluded(d, exclusions))
      .ForEach(d => relationalRemover.Visit(d));
  }

  private static bool IsExcluded(Declaration dec, List<string> exclusions)
  {
    return dec is NamedDeclaration namedDec && exclusions.Exists(e => namedDec.VerboseName.Contains(e));
  }
}