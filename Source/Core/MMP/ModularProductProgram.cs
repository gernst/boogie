using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection.Metadata;
using System.Security;
using Microsoft.Boogie;
using LocalVariable = Microsoft.Boogie.LocalVariable;
using Type = Microsoft.Boogie.Type;

namespace Core;

public class ModularProductProgram
{
  private List<BigBlock> _bigBlockList;
  private List<(Variable, Variable)> _localVariables;
  private List<(Variable, Variable)> _inParams;
  private Dictionary<string, (Variable, Variable)> _allVariables;

  public List<Variable> LocalVariables => FlattenVarList(_localVariables);

  public List<Variable> InParams => FlattenVarList(_inParams);

  public StmtList StructuredStmts => new(_bigBlockList, Token.NoToken);

  private Variable _majorP;
  private Variable _minorP;
  private MinorizeVisitor _minorizer;
  private string _minorPrefix = "minor_";
  private string _majorPrefix = "major_";
  private int _anon = 0;

  public ModularProductProgram(List<Variable> localVariables, StmtList structuredStmts, List<Variable> inParams)
  {
    _localVariables = DuplicateVariables(localVariables);
    _inParams = CalculateInParams(inParams);
    _minorizer = new MinorizeVisitor(_inParams.Concat(_localVariables).ToDictionary(t => t.Item1.Name, t => t));

    _bigBlockList = CalculateStructuredStmts(structuredStmts, new IdentifierExpr(Token.NoToken, _majorP),
      new IdentifierExpr(Token.NoToken, _minorP));
  }

  public void BuildProcProduct(Procedure proc)
  {
    proc.InParams = FlattenVarList(CalculateInParams(proc.InParams));
  }

  private List<(Variable, Variable)> CalculateInParams(List<Variable> inParams)
  {
    var duplicatedVariables = DuplicateVariables(inParams);
    _majorP = new Formal(Token.NoToken, new TypedIdent(Token.NoToken, "major_p", Type.Bool), true);
    _minorP = new Formal(Token.NoToken, new TypedIdent(Token.NoToken, "minor_p", Type.Bool), true);
    // _allVariables.Add(_majorP.Name, (_majorP, _minorP));
    duplicatedVariables.Insert(0, (_majorP, _minorP));
    return duplicatedVariables;
  }

  private List<(Variable, Variable)> DuplicateVariables(List<Variable> localVariables)
  {
    var duplicatedVariables = new List<(Variable, Variable)>();
    foreach (var v in localVariables)
    {
      Variable newVar;
      if (v is LocalVariable)
      {
        newVar = new LocalVariable(Token.NoToken,
          new TypedIdent(Token.NoToken, _minorPrefix + v.Name, v.TypedIdent.Type))
        {
          Name = _minorPrefix + v.Name
        };
      }
      else
      {
        newVar = new Formal(Token.NoToken,
          new TypedIdent(Token.NoToken, _minorPrefix + v.Name, v.TypedIdent.Type), true)
        {
          Name = _minorPrefix + v.Name
        };
      }
      duplicatedVariables.Add((v, newVar));

      // _allVariables.Add(v.Name, (v, newVar));
    }

    return duplicatedVariables;
  }

  public List<BigBlock> CalculateStructuredStmts(StmtList structuredStmts, Expr majorContext, Expr minorContext)
  {
    if (structuredStmts == null)
    {
      return null;
    }

    List<BigBlock> newBlocks = new List<BigBlock>();
    foreach (var bb in structuredStmts.BigBlocks)
    {
      newBlocks.AddRange(UpdateBlocks(bb, majorContext, minorContext));
      if (bb.ec is IfCmd)
      {
        IfCmd originalIfCmd = (IfCmd)bb.ec;
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
    }

    return newBlocks;
  }

  private List<BigBlock> UpdateBlocks(BigBlock bb, Expr majorContext, Expr minorContext)
  {
    List<BigBlock> updatedBlocks = new List<BigBlock>();
    foreach (var c in bb.simpleCmds)
    {
      if (c is AssignCmd)
      {
        AssignCmd assignCmd = (AssignCmd)c;

        List<AssignLhs> lhss = new List<AssignLhs>();
        for (int i = 0; i < assignCmd.Lhss.Count; i++)
        {
          var lhs = assignCmd.Lhss[i];
          var correspondingVariable =
            LocalVariables.Find(v => v.Name == _minorPrefix + lhs.DeepAssignedIdentifier.Name);
          var minorLhs = new SimpleAssignLhs(Token.NoToken,
            Expr.Ident(correspondingVariable));

          lhss.Add(minorLhs);
        }

        AssignCmd minorAssignCmd = new AssignCmd(Token.NoToken, lhss, assignCmd.Rhss);

        updatedBlocks.AddRange(CreateNewIfBigBlockPair(assignCmd, minorAssignCmd, majorContext, minorContext));
      }
      else if (c is AssertCmd or AssumeCmd)
      {
        Cmd solvedCmd = c switch
        {
          AssertCmd a => new AssertCmd(a.tok, SolveExpr(a.Expr, majorContext, minorContext)),
          AssumeCmd a => new AssumeCmd(a.tok, SolveExpr(a.Expr, majorContext, minorContext)),
          _ => throw new cce.UnreachableException()
        };  

        
        updatedBlocks.Add(new BigBlock(Token.NoToken, c.ToString().TrimEnd() + FreshAnon(), new List<Cmd> { solvedCmd }, null, null));
      }
      else
      {
        updatedBlocks.AddRange(CreateNewIfBigBlockPair(c, (Cmd)_minorizer.Visit(c.Clone()), majorContext,
          minorContext));
      }
    }

    return updatedBlocks;
  }

  private List<BigBlock> CreateNewIfBigBlockPair(Cmd majorCmd, Cmd minorCmd, Expr majorContext, Expr minorContext)
  {
    BigBlock majorInternalBlock = new BigBlock(
      Token.NoToken,
      _majorPrefix + majorCmd.ToString().TrimEnd() + "_Then" + FreshAnon(),
      new List<Cmd>() { majorCmd },
      null,
      null
    );
    IfCmd majorIf = new IfCmd(
      Token.NoToken,
      majorContext,
      new StmtList(new List<BigBlock>() { majorInternalBlock }, Token.NoToken),
      null,
      null
    );
    BigBlock majorBlock = new BigBlock(
      Token.NoToken,
      _majorPrefix + majorCmd.ToString().TrimEnd() + FreshAnon(),
      new List<Cmd>(),
      majorIf,
      null
    );

    BigBlock minorInternalBlock = new BigBlock(
      Token.NoToken,
      _minorPrefix + majorCmd.ToString().TrimEnd() + "_Then" + FreshAnon(),
      new List<Cmd>() { minorCmd },
      null,
      null
    );
    IfCmd minorIf = new IfCmd(
      Token.NoToken,
      minorContext,
      new StmtList(new List<BigBlock>() { minorInternalBlock }, Token.NoToken),
      null,
      null
    );
    BigBlock minorBlock = new BigBlock(
      Token.NoToken,
      _minorPrefix + majorCmd.ToString().TrimEnd() + FreshAnon(),
      new List<Cmd>(),
      minorIf,
      null
    );

    return new List<BigBlock>() { majorBlock, minorBlock };
  }

  private Expr SolveExpr(Expr expr, Expr majorContext, Expr minorContext)
  {
    if (RelationalChecker.IsRelational(expr))
    {
      switch (expr)
      {
       case LowExpr l:
         return Expr.Imp(Expr.And(majorContext, minorContext), Expr.Eq(l.Expr, _minorizer.VisitExpr(l.Expr)));
       case LowEventExpr l:
         return Expr.And(majorContext, minorContext);
       case NAryExpr n:
         if (!n.Type.Equals(Type.Bool)) {
            throw new ArgumentException();
         }
         return new NAryExpr(n.tok, n.Fun, n.Args.Select((e => SolveExpr(e, majorContext, minorContext))).ToList());
       case ExistsExpr e:
         return new ExistsExpr(e.tok, e.Dummies.SelectMany(v => new List<Variable> { v, _minorizer.VisitVariable(v)}).ToList(),
           SolveExpr(e.Body, majorContext, minorContext));
       case ForallExpr f:
         return new ExistsExpr(f.tok, f.Dummies.SelectMany(v => new List<Variable> { v, _minorizer.VisitVariable(v)}).ToList(),
           SolveExpr(f.Body, majorContext, minorContext));
       default:
         throw new ArgumentException();
      } 
    }

    var majorImp = Expr.Imp(majorContext, expr);
    var minorImp = Expr.Imp(minorContext, _minorizer.VisitExpr(expr));
    return Expr.And(majorImp, minorImp);
  }

  private int FreshAnon()
  {
    return _anon++;
  }

  private List<Variable> FlattenVarList(List<(Variable, Variable)> varList)
  { 
    return varList.SelectMany(tuple => new List<Variable> { tuple.Item1, tuple.Item2 })
        .ToList();
  }
}