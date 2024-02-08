using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Boogie;
using Type = Microsoft.Boogie.Type;

namespace Core.Security;

public static class Util
{
  public const string MinorPrefix = "minor_";

  public static List<(Variable, Variable)> DuplicateVariables(List<Variable> localVariables, MinorizeVisitor minorizer)
  {
    var duplicatedVariables = new List<(Variable, Variable)>();
    foreach (var v in localVariables)
    {
      Variable newVar;
      switch (v)
      {
        case LocalVariable:
          newVar = new LocalVariable(v.tok,
            new TypedIdent(v.TypedIdent.tok, MinorPrefix + v.Name, v.TypedIdent.Type))
          {
            Name = MinorPrefix + v.Name
          };
          break;
        case Formal formal:
          newVar = new Formal(formal.tok,
            new TypedIdent(formal.TypedIdent.tok, MinorPrefix + formal.Name, formal.TypedIdent.Type), formal.InComing)
          {
            Name = MinorPrefix + formal.Name
          };
          break;
        case BoundVariable:
          newVar = new BoundVariable(v.tok,
            new TypedIdent(v.TypedIdent.tok, MinorPrefix + v.Name, v.TypedIdent.Type))
          {
            Name = MinorPrefix + v.Name
          };
          break;
        default:
          var duplicator = new Duplicator();
          newVar = duplicator.VisitVariable(v);
          newVar.Name = MinorPrefix + newVar.Name;
          break;
      }

      if (v.TypedIdent.WhereExpr != null)
      {
        newVar.TypedIdent.WhereExpr = minorizer.AddTemporaryVariables(new List<(Variable, Variable)> { (v, newVar) })
          .VisitExpr(v.TypedIdent.WhereExpr);
      }

      duplicatedVariables.Add((v, newVar));
    }

    return duplicatedVariables;
  }
  public static List<A> FlattenLists<A>(List<(A, A)> args)
  {
    return args.SelectMany(tuple => new List<A> { tuple.Item1, tuple.Item2 })
      .ToList();
  }

  public static List<Variable> FlattenVarList(List<(Variable, Variable)> varList)
  {
    return varList.SelectMany(tuple => new List<Variable> { tuple.Item1, tuple.Item2 })
      .ToList();
  }

  public static void SplitVariablesByContext(List<Variable> variables, out List<Variable> majorVars, out List<Variable> minorVars)
  {
    majorVars = variables.Where((item, index) => index % 2 == 0).ToList();
    minorVars = variables.Where((item, index) => index % 2 != 0).ToList();
  }

  public static List<(Variable, Variable)> CalculateInParams(List<Variable> inParams, MinorizeVisitor minorizer)
  {
    var duplicatedVariables = Util.DuplicateVariables(inParams, minorizer);
    return duplicatedVariables;
  }

  public static Expr SolveExpr(Program program, Expr expr, MinorizeVisitor minorizer)
  {
    if (RelationalChecker.IsRelational(program, expr))
    {
      switch (expr)
      {
        case LowExpr l:
          return Expr.Eq(l.Expr, minorizer.VisitExpr(l.Expr));
        case NAryExpr n:
          // if (!n.Type.Equals(Type.Bool))
          // {
          //   throw new ArgumentException();
          // }
          // we just called isRelational, so .Func is guaranteed to be set for FunctionCalls

          var funCall = n.Fun as FunctionCall;
          bool relational = false;

          if (funCall != null && funCall.Func.CheckBooleanAttribute("relational", ref relational) && relational)
          {
            var minorArgs = minorizer.VisitExprSeq(n.Args);
            var args = Util.FlattenLists<Expr>(n.Args.Zip(minorArgs).ToList());
            return new NAryExpr(n.tok, n.Fun, args);
          }
          else
          {
            return new NAryExpr(n.tok, n.Fun, n.Args.Select((e => SolveExpr(program, e, minorizer))).ToList());
          }
        case ExistsExpr e:
          {
            var duplicatedBounds = Util.DuplicateVariables(e.Dummies, minorizer);
            var adaptedMinorizer = minorizer.AddTemporaryVariables(duplicatedBounds);
            return new ExistsExpr(e.tok, Util.FlattenVarList(duplicatedBounds), SolveExpr(program, e.Body, adaptedMinorizer));
          }
        case ForallExpr f:
          {
            var duplicatedBounds = Util.DuplicateVariables(f.Dummies, minorizer);
            var adaptedMinorizer = minorizer.AddTemporaryVariables(duplicatedBounds);
            return new ForallExpr(f.tok, f.TypeParameters, Util.FlattenVarList(duplicatedBounds), SolveExpr(program, f.Body, adaptedMinorizer));
          }
        default:
          throw new ArgumentException(expr.ToString());
      }
    }

    return Expr.And(expr, minorizer.VisitExpr(expr));
  }
}