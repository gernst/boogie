using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Boogie;
using Type = Microsoft.Boogie.Type;

namespace Core.Security;

public static class Util
{
  public const string MinorPrefix = "minor_";
  public const string MajorPrefix = "major_";
  public static readonly Formal MajorP = new Formal(Token.NoToken, new TypedIdent(Token.NoToken, "major_p", Type.Bool), true);
  public static readonly Formal MinorP = new Formal(Token.NoToken, new TypedIdent(Token.NoToken, "minor_p", Type.Bool), true);

  public static List<(Variable, Variable)> DuplicateVariables(List<Variable> localVariables)
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

      

      duplicatedVariables.Add((v, newVar));
    }

    return duplicatedVariables;
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

  public static List<(Variable, Variable)> CalculateInParams(List<Variable> inParams)
  {
    var duplicatedVariables = Util.DuplicateVariables(inParams);
    duplicatedVariables.Insert(0, (Util.MajorP, Util.MinorP));
    return duplicatedVariables;
  }

  public static Expr SolveExpr(Expr expr, Expr majorContext, Expr minorContext, MinorizeVisitor minorizer)
  {
    if (RelationalChecker.IsRelational(expr))
    {
      switch (expr)
      {
        case LowExpr l:
          return Expr.Imp(Expr.And(majorContext, minorContext), Expr.Eq(l.Expr, minorizer.VisitExpr(l.Expr)));
        case LowEventExpr l:
          return Expr.And(majorContext, minorContext);
        case NAryExpr n:
          // if (!n.Type.Equals(Type.Bool))
          // {
          //   throw new ArgumentException();
          // }

          return new NAryExpr(n.tok, n.Fun, n.Args.Select((e => SolveExpr(e, majorContext, minorContext, minorizer))).ToList());
        case ExistsExpr e:
        {
          var duplicatedBounds = Util.DuplicateVariables(e.Dummies);
          var adaptedMinorizer = minorizer.AddTemporaryVariables(duplicatedBounds);
          return new ExistsExpr(e.tok, Util.FlattenVarList(duplicatedBounds), SolveExpr(e.Body, majorContext, minorContext, adaptedMinorizer));
        }
        case ForallExpr f:
        {
          var duplicatedBounds = Util.DuplicateVariables(f.Dummies);
          var adaptedMinorizer = minorizer.AddTemporaryVariables(duplicatedBounds);
          return new ForallExpr(f.tok, Util.FlattenVarList(duplicatedBounds), SolveExpr(f.Body, majorContext, minorContext, adaptedMinorizer));
        }
        default:
          throw new ArgumentException();
      }
    }

    if (expr is QuantifierExpr q)
    {
      var duplicatedBounds = Util.DuplicateVariables(q.Dummies);
      var adaptedMinorizer = minorizer.AddTemporaryVariables(duplicatedBounds);
      q.Dummies = Util.FlattenVarList(duplicatedBounds);
      var majorImp = Expr.Imp(majorContext, q.Body);
      var minorImp = Expr.Imp(minorContext, adaptedMinorizer.VisitExpr(q.Body));

      q.Body = Expr.And(majorImp, minorImp);
      return q;
    }
    else
    {
      var majorImp = Expr.Imp(majorContext, expr);
      var minorImp = Expr.Imp(minorContext, minorizer.VisitExpr(expr));
      return Expr.And(majorImp, minorImp);
    }
  }
}