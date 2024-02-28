using System.Collections.Generic;

namespace Microsoft.Boogie {

  public class RelationalChecker : ReadOnlyVisitor {
    Program _program;

    private bool isRelational;

    private RelationalChecker(Program program) {
      _program = program;
      isRelational = false;
    }

    public static bool IsRelational(Program program, Absy absy) {
      var checker = new RelationalChecker(program);
      checker.Visit(absy);
      return checker.isRelational;
    }

    public static bool IsRelationalFunction(Function fun) {
      var result = false;
      fun.CheckBooleanAttribute("relational", ref result);
      return result;
    }

    public static bool IsExcludedRelationalProcedure(Declaration dec, List<string> exclusions) {
      return dec is NamedDeclaration namedDec && exclusions.Exists(e => namedDec.VerboseName.Contains(e));
    }

    public override Expr VisitLowExpr(LowExpr node) {
      isRelational = true;
      return base.VisitLowExpr(node);
    }

    public override Expr VisitLowEventExpr(LowEventExpr node) {
      isRelational = true;
      return base.VisitLowEventExpr(node);
    }

    public override Expr VisitNAryExpr(NAryExpr node) {
      var funCall = node.Fun as FunctionCall;

      if (funCall == null) {
        return base.VisitNAryExpr(node);
      } else {
        funCall.Func = _program.FindFunction(funCall.FunctionName);
        bool relational = false;
        funCall.Func.CheckBooleanAttribute("relational", ref relational);

        isRelational |= relational;

        return base.VisitNAryExpr(node);
      }
    }
  }
}