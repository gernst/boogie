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

public class FunctionMpp
{
  public static void CalculateFunctionMpp(Program program, Function function)
  {
    var minorizer = new MinorizeVisitor(new Dictionary<string, (Variable, Variable)>());
    var inParams = Util.CalculateInParams(function.InParams, minorizer);
    function.InParams = Util.FlattenVarList(inParams);
  }
}