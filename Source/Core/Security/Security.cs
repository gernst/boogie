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
  }
}