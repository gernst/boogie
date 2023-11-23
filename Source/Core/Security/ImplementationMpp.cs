using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Boogie;
using LocalVariable = Microsoft.Boogie.LocalVariable;
using Type = Microsoft.Boogie.Type;
using Util = Core.Security.Util;

namespace Core;

public class ImplementationMpp
{
  private List<BigBlock> _bigBlockList;
  private List<(Variable, Variable)> _localVariables;
  private List<(Variable, Variable)> _inParams;
  private List<(Variable, Variable)> _outParams;

  public List<Variable> LocalVariables => Util.FlattenVarList(_localVariables);

  public List<Variable> InParams => Util.FlattenVarList(_inParams);

  public List<Variable> OutParams => Util.FlattenVarList(_outParams);

  public StmtList StructuredStmts => new(_bigBlockList, Token.NoToken);

  public Implementation Implementation { get; }

  private MinorizeVisitor _minorizer;
  private IdentifierTypeVisitor _typeVisitor;
  private int _anon = 0;

  public ImplementationMpp(Implementation implementation, Dictionary<string, (Variable, Variable)> globalVariableDict)
  {
    var minorizer = new MinorizeVisitor(globalVariableDict);
    _localVariables = Util.DuplicateVariables(implementation.LocVars, minorizer);
    _inParams = Util.CalculateInParams(implementation.InParams, minorizer);
    _outParams = Util.DuplicateVariables(implementation.OutParams, minorizer);

    _minorizer = minorizer.AddTemporaryVariables(_inParams.Concat(_outParams).Concat(_localVariables)
      .ToList());
    _typeVisitor = new IdentifierTypeVisitor(implementation.InParams.Concat(implementation.LocVars).ToList());

    _bigBlockList = CalculateStructuredStmts(implementation.StructuredStmts,
      new IdentifierExpr(Token.NoToken, Util.MajorP),
      new IdentifierExpr(Token.NoToken, Util.MinorP));

    Implementation = new Implementation(
      implementation.tok,
      implementation.Name,
      implementation.TypeParameters,
      Util.FlattenVarList(_inParams),
      Util.FlattenVarList(_outParams),
      Util.FlattenVarList(_localVariables),
      new StmtList(_bigBlockList, Token.NoToken));
  }

  public List<BigBlock> CalculateStructuredStmts(StmtList structuredStmts, Expr majorContext, Expr minorContext)
  {
    if (structuredStmts == null)
    {
      return null;
    }

    var newBlocks = new List<BigBlock>();

    foreach (var bb in structuredStmts.BigBlocks)
    {
      newBlocks.AddRange(UpdateBlocks(bb, majorContext, minorContext));
      if (bb.ec is IfCmd originalIfCmd)
      {
        var majorThenGuard = Expr.And(majorContext, originalIfCmd.Guard);
        var minorThenGuard = Expr.And(minorContext, _minorizer.VisitExpr(originalIfCmd.Guard));
        newBlocks.AddRange(CalculateStructuredStmts(originalIfCmd.thn, majorThenGuard, minorThenGuard));

        if (originalIfCmd.elseBlock == null)
        {
          continue;
        }

        var majorElseGuard = Expr.And(majorContext, Expr.Not(originalIfCmd.Guard));
        var minorElseGuard = Expr.And(minorContext, Expr.Not(_minorizer.VisitExpr(originalIfCmd.Guard)));
        newBlocks.AddRange(CalculateStructuredStmts(originalIfCmd.elseBlock, majorElseGuard, minorElseGuard));
      }
      else if (bb.ec is WhileCmd whileCmd)
      {
        var termVar = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, "term" + FreshAnon(), Type.Bool));

        AddLocalVars(new List<Variable> { termVar });
        var termIdentExpr = new IdentifierExpr(Token.NoToken, termVar);
        whileCmd.TerminationExpr = termIdentExpr;

        var majorTermAssignment = AssignTermVariable(termIdentExpr, true);
        var minorTermAssignment =
          AssignTermVariable(
            (IdentifierExpr)_minorizer.VisitIdentifierExpr(termIdentExpr),
            true);
        var termBlocks = CreateNewIfBigBlockPair(majorTermAssignment, minorTermAssignment, majorContext, minorContext);
        newBlocks.AddRange(termBlocks);

        whileCmd.Invariants.ForEach(x => { x.Expr = Util.SolveExpr(x.Expr, majorContext, minorContext, _minorizer); });

        var majorWhileGuard = Expr.And(new List<Expr> { majorContext, whileCmd.Guard, termIdentExpr });
        var minorWhileGuard = Expr.And(new List<Expr>
          { minorContext, _minorizer.VisitExpr(whileCmd.Guard), _minorizer.VisitExpr(termIdentExpr) });
        whileCmd.Guard = Expr.Or(majorWhileGuard, minorWhileGuard);
        whileCmd.Body = new StmtList(CalculateStructuredStmts(whileCmd.Body, majorWhileGuard, minorWhileGuard),
          whileCmd.Body.EndCurly);

        newBlocks.Add(new BigBlock(Token.NoToken, bb.LabelName, new List<Cmd>(), whileCmd, null));
      }
      else if (bb.ec is BreakCmd breakCmd)
      {
        var outerWhileCmd = (WhileCmd)breakCmd.BreakEnclosure.ec;
        var majorTerminationExpr = outerWhileCmd.TerminationExpr;
        var minorTerminationExpr = (IdentifierExpr)_minorizer.VisitIdentifierExpr(majorTerminationExpr);
        var majorBreak = AssignTermVariable(majorTerminationExpr, false);
        var minorBreak = AssignTermVariable(minorTerminationExpr, false);
        var breakInvariant = Expr.Imp(Expr.Not(majorTerminationExpr), RemoveTerminationExpression(majorContext, majorTerminationExpr));
        outerWhileCmd.Invariants.Add(new AssertCmd(Token.NoToken, Util.SolveExpr(breakInvariant, new IdentifierExpr(Token.NoToken, Util.MajorP),
          new IdentifierExpr(Token.NoToken, Util.MinorP), _minorizer)));
        var breakBlocks = CreateNewIfBigBlockPair(majorBreak, minorBreak, majorContext, minorContext);
        newBlocks.AddRange(breakBlocks);
      }
    }

    return newBlocks;
  }

  private List<BigBlock> UpdateBlocks(BigBlock bb, Expr majorContext, Expr minorContext)
  {
    var updatedBlocks = new List<BigBlock>();
    foreach (var c in bb.simpleCmds)
    {
      switch (c)
      {
        case AssignCmd assignCmd:
        {
          var lhss = new List<AssignLhs>();
          foreach (var lhs in assignCmd.Lhss)
          {
            var correspondingVariable = (IdentifierExpr)_minorizer.VisitIdentifierExpr(lhs.DeepAssignedIdentifier);
            var minorLhs = new SimpleAssignLhs(Token.NoToken,
              correspondingVariable);

            lhss.Add(minorLhs);
          }

          var minorAssignCmd = new AssignCmd(Token.NoToken, lhss,
            assignCmd.Rhss.Select(e => _minorizer.VisitExpr(e)).ToList());

          updatedBlocks.AddRange(CreateNewIfBigBlockPair(assignCmd, minorAssignCmd, majorContext, minorContext));
          break;
        }
        case AssertCmd or AssumeCmd:
        {
          Cmd solvedCmd = c switch
          {
            AssertCmd a => new AssertCmd(a.tok, Util.SolveExpr(a.Expr, majorContext, minorContext, _minorizer)),
            AssumeCmd a => new AssumeCmd(a.tok, Util.SolveExpr(a.Expr, majorContext, minorContext, _minorizer)),
            _ => throw new cce.UnreachableException()
          };


          updatedBlocks.Add(new BigBlock(Token.NoToken, null, new List<Cmd> { solvedCmd },
            null, null));
          break;
        }
        case CallCmd callCmd:
        {
          var tempInVars = callCmd.Ins.Select((e, i) =>
          {
            _typeVisitor.VisitExpr(e);
            e.Resolve(new ResolutionContext(null, null));
            e.Typecheck(new TypecheckingContext(null, null));
            var typedIdent = new TypedIdent(Token.NoToken, "a" + i + "_temp_in" + FreshAnon(), e.Type);
            return new LocalVariable(Token.NoToken, typedIdent);
          }).ToList().ConvertAll(v => (Variable)v);
          var dupTempInVars = AddLocalVars(tempInVars);

          var majorInTempLhss = dupTempInVars
            .Select(x => (AssignLhs)new SimpleAssignLhs(x.Item1.tok, Expr.Ident(x.Item1))).ToList();
          var majorInExprs = callCmd.Ins;
          var majorInAssignCmd = new AssignCmd(Token.NoToken, majorInTempLhss, majorInExprs);

          var minorInTempLhss = dupTempInVars
            .Select(x => (AssignLhs)new SimpleAssignLhs(x.Item2.tok, Expr.Ident(x.Item2))).ToList();
          var minorInExprs = callCmd.Ins.Select(_minorizer.VisitExpr).ToList();
          var minorInAssignCmd = new AssignCmd(Token.NoToken, minorInTempLhss, minorInExprs);

          var tempInAssignmentBBs =
            CreateNewIfBigBlockPair(majorInAssignCmd, minorInAssignCmd, majorContext, minorContext);

          var tempOutVars = callCmd.Outs.Select(e =>
          {
            var typedIdent = new TypedIdent(Token.NoToken, e.Name + "_temp_out" + FreshAnon(),
              GetTypeFromVarName(e.Name));
            return new LocalVariable(Token.NoToken, typedIdent);
          }).ToList().ConvertAll(v => (Variable)v);
          var dupTempOutVars = AddLocalVars(tempOutVars);

          var majorOutTempExprs = dupTempOutVars.Select(x => (Expr)Expr.Ident(x.Item1)).ToList();
          var majorOutLhss = callCmd.Outs.Select(e => (AssignLhs)new SimpleAssignLhs(e.tok, e)).ToList();
          var majorOutAssignCmd = new AssignCmd(Token.NoToken, majorOutLhss, majorOutTempExprs);

          var minorOutTempExprs = dupTempOutVars.Select(x => (Expr)Expr.Ident(x.Item2)).ToList();
          var minorOutLhss = callCmd.Outs
            .Select(e =>
              (AssignLhs)new SimpleAssignLhs(e.tok, (IdentifierExpr)_minorizer.VisitIdentifierExpr(e))
            )
            .ToList();
          var minorOutAssignCmd = new AssignCmd(Token.NoToken, minorOutLhss, minorOutTempExprs);

          var tempOutAssignmentBBs =
            CreateNewIfBigBlockPair(majorOutAssignCmd, minorOutAssignCmd, majorContext, minorContext);

          callCmd.Outs = Util.FlattenVarList(dupTempOutVars).Select(Expr.Ident).ToList();
          callCmd.Ins = Util.FlattenVarList(dupTempInVars)
            .Select(Expr.Ident)
            .Prepend(minorContext)
            .Prepend(majorContext)
            .ToList();
          var callBB = new BigBlock(
            Token.NoToken,
            null,
            new List<Cmd> { callCmd },
            null, null);

          var allBBs = new List<BigBlock>();
          allBBs.AddRange(tempInAssignmentBBs);
          allBBs.Add(callBB);
          allBBs.AddRange(tempOutAssignmentBBs);
          var ifAnyContext = new IfCmd(
            Token.NoToken,
            Expr.Or(majorContext, minorContext),
            new StmtList(allBBs, Token.NoToken),
            null, null);

          var newBB = new BigBlock(Token.NoToken, null, new List<Cmd>(), ifAnyContext, null);
          updatedBlocks.Add(newBB);
          break;
        }
        case CommentCmd: break;
        default:
          updatedBlocks.AddRange(CreateNewIfBigBlockPair(c, (Cmd)_minorizer.Visit(c.Clone()), majorContext,
            minorContext));
          break;
      }
    }

    return updatedBlocks;
  }

  private IEnumerable<BigBlock> CreateNewIfBigBlockPair(Cmd majorCmd, Cmd minorCmd, Expr majorContext,
    Expr minorContext)
  {
    var majorInternalBlock = new BigBlock(
      Token.NoToken, null,
      new List<Cmd>() { majorCmd },
      null,
      null
    );
    var majorIf = new IfCmd(
      Token.NoToken,
      majorContext,
      new StmtList(new List<BigBlock>() { majorInternalBlock }, Token.NoToken),
      null,
      null
    );
    var majorBlock = new BigBlock(
      Token.NoToken, null,
      new List<Cmd>(),
      majorIf,
      null
    );

    var minorInternalBlock = new BigBlock(
      Token.NoToken, null,
      new List<Cmd>() { minorCmd },
      null,
      null
    );
    var minorIf = new IfCmd(
      Token.NoToken,
      minorContext,
      new StmtList(new List<BigBlock>() { minorInternalBlock }, Token.NoToken),
      null,
      null
    );
    var minorBlock = new BigBlock(
      Token.NoToken, null,
      new List<Cmd>(),
      minorIf,
      null
    );

    return new List<BigBlock>() { majorBlock, minorBlock };
  }

  private int FreshAnon()
  {
    return _anon++;
  }

  private List<(Variable, Variable)> AddLocalVars(List<Variable> variables)
  {
    var duplicatedVars = Util.DuplicateVariables(variables, _minorizer);
    _minorizer = _minorizer.AddTemporaryVariables(duplicatedVars);
    _localVariables.AddRange(duplicatedVars);
    return duplicatedVars;
  }

  private Type GetTypeFromVarName(String name)
  {
    return _localVariables.Select(x => x.Item1).First(x => x.Name.Equals(name)).TypedIdent.Type;
  }

  private AssignCmd AssignTermVariable(IdentifierExpr expr, bool value)
  {
    return new AssignCmd(
      Token.NoToken,
      new List<AssignLhs> { new SimpleAssignLhs(Token.NoToken, expr) },
      new List<Expr> { value ? Expr.True : Expr.False }
    );
  }

  private Expr RemoveTerminationExpression(Expr expr, IdentifierExpr terminationExpr)
  {
    if (expr is NAryExpr e && e.Fun.FunctionName == "&&")
    {
      if (e.Args[0] is IdentifierExpr idExpr1 && idExpr1.Name == terminationExpr.Name)
      {
        return e.Args[1];
      }
      
      if (e.Args[1] is IdentifierExpr idExpr2 && idExpr2.Name == terminationExpr.Name)
      {
        return e.Args[0];
      }

      return Expr.And(RemoveTerminationExpression(e.Args[0], terminationExpr),
        RemoveTerminationExpression(e.Args[1], terminationExpr));
    }
    return expr;
  }
}