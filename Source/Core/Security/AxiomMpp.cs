using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Security;
using Core.Security;
using Microsoft.Boogie;
using LocalVariable = Microsoft.Boogie.LocalVariable;
using Type = Microsoft.Boogie.Type;
using Util = Core.Security.Util;

namespace Core;

public class AxiomMpp
{
  public static Axiom CalculateAxiomMpp(Program program, Axiom axiom, Dictionary<string, (Variable, Variable)> globalVariableDict)
  {
    var minorizer = new MinorizeVisitor(globalVariableDict);
    var relationalAxiom = new Axiom(axiom.tok, Util.SolveExpr(program, axiom.Expr, minorizer));  
    // remove relational Expressions from original Axioms
    var relationalRemover = new RelationalRemover();
    relationalRemover.VisitAxiom(axiom);
    
    return relationalAxiom;
  }
}