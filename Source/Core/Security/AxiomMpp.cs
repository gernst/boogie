using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Security;
using Microsoft.Boogie;
using LocalVariable = Microsoft.Boogie.LocalVariable;
using Type = Microsoft.Boogie.Type;
using Util = Core.Security.Util;

namespace Core;

public class AxiomMpp
{
  public static void CalculateAxiomMpp(Program program, Axiom axiom, Dictionary<string, (Variable, Variable)> globalVariableDict)
  {
    var minorizer = new MinorizeVisitor(globalVariableDict);
    axiom.Expr = Util.SolveExpr(program, axiom.Expr, minorizer);
  }
}