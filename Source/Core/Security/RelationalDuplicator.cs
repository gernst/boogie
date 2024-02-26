using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Boogie {

  public static class RelationalDuplicator {
    public const string MinorPrefix = "minor_";
    public const string RelationalSuffix = "_relational";

    public static List<(Variable, Variable)> DuplicateVariables(List<Variable> localVariables, MinorizeVisitor minorizer) {
      var duplicatedVariables = new List<(Variable, Variable)>();
      var updatedMinorizer = minorizer.AddTemporaryVariables(new List<(Variable, Variable)>());
      foreach (var v in localVariables) {
        Variable newVar;
        switch (v) {
          case LocalVariable:
            newVar = new LocalVariable(v.tok,
              new TypedIdent(v.TypedIdent.tok, MinorPrefix + v.Name, v.TypedIdent.Type)) {
              Name = MinorPrefix + v.Name
            };
            break;
          case Formal formal:
            var newName = formal.Name.Length > 0 ? MinorPrefix + formal.Name : "";
            newVar = new Formal(formal.tok,
              new TypedIdent(formal.TypedIdent.tok, newName, formal.TypedIdent.Type), formal.InComing);
            break;
          case BoundVariable:
            newVar = new BoundVariable(v.tok,
              new TypedIdent(v.TypedIdent.tok, MinorPrefix + v.Name, v.TypedIdent.Type)) {
              Name = MinorPrefix + v.Name
            };
            break;
          default:
            var duplicator = new Duplicator();
            newVar = duplicator.VisitVariable(v);
            newVar.Name = MinorPrefix + newVar.Name;
            break;
        }

        if (v.Name.Length > 0) {
          updatedMinorizer = updatedMinorizer.AddTemporaryVariables(new List<(Variable, Variable)> { (v, newVar) });
        }

        if (v.TypedIdent.WhereExpr != null) {
          newVar.TypedIdent.WhereExpr = updatedMinorizer
            .VisitExpr(v.TypedIdent.WhereExpr);
        }

        duplicatedVariables.Add((v, newVar));
      }

      return duplicatedVariables;
    }
    public static List<A> FlattenLists<A>(List<(A, A)> args) {
      return args.SelectMany(tuple => new List<A> { tuple.Item1, tuple.Item2 })
        .ToList();
    }

    public static List<Variable> FlattenVarList(List<(Variable, Variable)> varList) {
      return varList.SelectMany(tuple => new List<Variable> { tuple.Item1, tuple.Item2 })
        .ToList();
    }

    public static void SplitVariablesByContext(List<Variable> variables, out List<Variable> majorVars, out List<Variable> minorVars) {
      majorVars = variables.Where((item, index) => index % 2 == 0).ToList();
      minorVars = variables.Where((item, index) => index % 2 != 0).ToList();
    }

    public static List<(Variable, Variable)> CalculateInParams(List<Variable> inParams, MinorizeVisitor minorizer) {
      var duplicatedVariables = RelationalDuplicator.DuplicateVariables(inParams, minorizer);
      return duplicatedVariables;
    }

    public static Expr SolveExpr(Program program, Expr expr, MinorizeVisitor minorizer) {
      if (RelationalChecker.IsRelational(program, expr)) {
        switch (expr) {
          case LowExpr l:
            return Expr.Eq(l.Expr, minorizer.VisitExpr(l.Expr));
          case NAryExpr n:
            // we just called isRelational, so .Func is guaranteed to be set for FunctionCalls
            var funCall = n.Fun as FunctionCall;
            bool relational = false;

            if (funCall != null && funCall.Func.CheckBooleanAttribute("relational", ref relational) && relational) {
              var relationalFunction = program.FindFunction(funCall.FunctionName + RelationalSuffix);
              var minorArgs = minorizer.VisitExprSeq(n.Args);
              var args = RelationalDuplicator.FlattenLists<Expr>(n.Args.Zip(minorArgs).ToList());
              return new NAryExpr(n.tok, new FunctionCall(relationalFunction), args);
            } else {
              return new NAryExpr(n.tok, n.Fun, n.Args.Select((e => SolveExpr(program, e, minorizer))).ToList());
            }
          case ExistsExpr e: {
              var duplicatedBounds = RelationalDuplicator.DuplicateVariables(e.Dummies, minorizer);
              var adaptedMinorizer = minorizer.AddTemporaryVariables(duplicatedBounds);
              return new ExistsExpr(
                e.tok,
                RelationalDuplicator.FlattenVarList(duplicatedBounds),
                SolveTrigger(e.Triggers, program, adaptedMinorizer),
                SolveExpr(program, e.Body, adaptedMinorizer));
            }
          case ForallExpr f: {
              var duplicatedBounds = RelationalDuplicator.DuplicateVariables(f.Dummies, minorizer);
              var adaptedMinorizer = minorizer.AddTemporaryVariables(duplicatedBounds);
              return new ForallExpr(
                f.tok,
                RelationalDuplicator.FlattenVarList(duplicatedBounds),
                SolveTrigger(f.Triggers, program, adaptedMinorizer),
                SolveExpr(program, f.Body, adaptedMinorizer));
            }
          case LetExpr l: {
              var duplicatedBounds = RelationalDuplicator.DuplicateVariables(l.Dummies, minorizer);
              var adaptedMinorizer = minorizer.AddTemporaryVariables(duplicatedBounds);
              var minorizedRhss = adaptedMinorizer.VisitExprSeq(l.Rhss);
              return new LetExpr(
                l.tok,
                RelationalDuplicator.FlattenVarList(duplicatedBounds),
                FlattenLists(l.Rhss.Zip(minorizedRhss).ToList()),
                null,
                SolveExpr(program, l.Body, adaptedMinorizer));
            }
          default:
            throw new ArgumentException(expr.ToString());
        }
      }

      return Expr.And(expr, minorizer.VisitExpr(expr));
    }

    private static Trigger SolveTrigger(Trigger trigger, Program program, MinorizeVisitor minorizer) {
      if (trigger == null) {
        return null;
      }

      Trigger origNext = trigger.Next;
      Trigger newNext = null;

      if (origNext != null) {
        newNext = SolveTrigger(origNext, program, minorizer);
      }

      return new Trigger(
        trigger.tok,
        trigger.Pos,
        trigger.Tr.Select(tr => SolveExpr(program, tr, minorizer)),
        newNext
        );
    }
  }
}