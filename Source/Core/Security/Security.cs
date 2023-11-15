using System.Collections.Generic;
using System.Linq;
using Microsoft.Boogie;

namespace Core.Security;

public static class Security
{

  public static void CalculateMpp(Program program)
  {

    var newImplementations = program.Implementations.Select(i => new ImplementationMpp(i).Implementation).ToList(); 
    program.RemoveTopLevelDeclarations(dec => dec is Implementation);
    program.AddTopLevelDeclarations(newImplementations);
    
    // foreach (var implementation in program.Implementations)
    // {
    //   var implementationMpp = new ImplementationMpp(implementation);
    //   implementation.LocVars = implementationMpp.LocalVariables;
    //   implementation.InParams = implementationMpp.InParams;
    //   implementation.OutParams = implementationMpp.OutParams;
    //   implementation.StructuredStmts = implementationMpp.StructuredStmts;
    //   BigBlocksResolutionContext ctx = new BigBlocksResolutionContext(implementation.StructuredStmts, new Errors());
    //   implementation.Blocks = ctx.Blocks;
    // }

    foreach (var proc in program.Procedures)
    {
      ProcedureMpp.CalculateProcedureMpp(proc);
    }

    var minorGlobals = program.GlobalVariables
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
        return minorGlob;
      })
      .ToList();
    program.AddTopLevelDeclarations(minorGlobals);
  }
}