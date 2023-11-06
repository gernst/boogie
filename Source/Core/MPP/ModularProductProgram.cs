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

namespace Core;

public class ModularProductProgram
{
  private List<BigBlock> _bigBlockList;
  private List<(Variable, Variable)> _localVariables;
  private List<(Variable, Variable)> _inParams;
  private List<(Variable, Variable)> _outParams;
  private Dictionary<string, (Variable, Variable)> _allVariables;

  public List<Variable> LocalVariables => FlattenVarList(_localVariables);

  public List<Variable> InParams => FlattenVarList(_inParams);
  
  public List<Variable> OutParams => FlattenVarList(_outParams);

  public StmtList StructuredStmts => new(_bigBlockList, Token.NoToken);

  private Variable _majorP;
  private Variable _minorP;
  private MinorizeVisitor _minorizer;
  private IdentifierTypeVisitor _typeVisitor;
  private static string _minorPrefix = "minor_";
  private static string _majorPrefix = "major_";
  private int _anon = 0;

  public ModularProductProgram(List<Variable> localVariables, StmtList structuredStmts, List<Variable> inParams, List<Variable> outParams)
  {
    _localVariables = DuplicateVariables(localVariables);
    _inParams = CalculateInParams(inParams);
    _outParams = DuplicateVariables(outParams);
    _minorizer = new MinorizeVisitor(_inParams.Concat(_outParams).Concat(_localVariables).ToDictionary(t => t.Item1.Name, t => t));
    _typeVisitor = new IdentifierTypeVisitor(inParams.Concat(localVariables).ToList());

    _bigBlockList = CalculateStructuredStmts(structuredStmts, new IdentifierExpr(Token.NoToken, _majorP),
      new IdentifierExpr(Token.NoToken, _minorP));
  }

  public void BuildProcProduct(Procedure proc)
  {
    proc.InParams = FlattenVarList(CalculateInParams(proc.InParams));
    proc.OutParams = FlattenVarList(DuplicateVariables(proc.OutParams));
    foreach (var req in proc.Requires)
    {
      req.Condition = SolveExpr(req.Condition, new IdentifierExpr(Token.NoToken, _majorP),
        new IdentifierExpr(Token.NoToken, _minorP), _minorizer);
    }
    foreach (var ens in proc.Ensures)
    {
      ens.Condition = SolveExpr(ens.Condition, new IdentifierExpr(Token.NoToken, _majorP),
        new IdentifierExpr(Token.NoToken, _minorP), _minorizer);
    }
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
            new TypedIdent(v.TypedIdent.tok, _minorPrefix + v.Name, v.TypedIdent.Type))
          {
            Name = _minorPrefix + v.Name
          };
          break;
        case Formal formal:
          newVar = new Formal(formal.tok,
            new TypedIdent(formal.TypedIdent.tok, _minorPrefix + formal.Name, formal.TypedIdent.Type), formal.InComing)
          {
            Name = _minorPrefix + formal.Name
          };
          break;
        case BoundVariable:
          newVar = new BoundVariable(v.tok,
            new TypedIdent(v.TypedIdent.tok, _minorPrefix + v.Name, v.TypedIdent.Type))
          {
            Name = _minorPrefix + v.Name
          };
          break;
        default:
          var duplicator = new Duplicator();
          newVar = duplicator.VisitVariable(v);
          newVar.Name = _minorPrefix + newVar.Name;
          break;
      }

      

      duplicatedVariables.Add((v, newVar));
    }

    return duplicatedVariables;
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
        whileCmd.Invariants.ForEach(x =>
        {
          x.Expr = SolveExpr(x.Expr, majorContext, minorContext, _minorizer);
        });

        var majorWhileGuard = Expr.And(majorContext, whileCmd.Guard);
        var minorWhileGuard = Expr.And(minorContext, _minorizer.VisitExpr(whileCmd.Guard));
        whileCmd.Guard = Expr.Or(majorWhileGuard, minorWhileGuard);
        whileCmd.Body = new StmtList(CalculateStructuredStmts(whileCmd.Body, majorWhileGuard, minorWhileGuard),
          whileCmd.Body.EndCurly);

        newBlocks.Add(new BigBlock(Token.NoToken, bb.LabelName, new List<Cmd>(), whileCmd, null));
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

          var minorAssignCmd = new AssignCmd(Token.NoToken, lhss, assignCmd.Rhss.Select(e => _minorizer.VisitExpr(e)).ToList());

          updatedBlocks.AddRange(CreateNewIfBigBlockPair(assignCmd, minorAssignCmd, majorContext, minorContext));
          break;
        }
        case AssertCmd or AssumeCmd:
        {
          Cmd solvedCmd = c switch
          {
            AssertCmd a => new AssertCmd(a.tok, SolveExpr(a.Expr, majorContext, minorContext, _minorizer)),
            AssumeCmd a => new AssumeCmd(a.tok, SolveExpr(a.Expr, majorContext, minorContext, _minorizer)),
            _ => throw new cce.UnreachableException()
          };


          updatedBlocks.Add(new BigBlock(Token.NoToken, c.ToString().TrimEnd() + FreshAnon(), new List<Cmd> { solvedCmd },
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
        
          var majorInTempLhss = dupTempInVars.Select(x => (AssignLhs)new SimpleAssignLhs(x.Item1.tok, Expr.Ident(x.Item1))).ToList();
          var majorInExprs = callCmd.Ins;
          var majorInAssignCmd = new AssignCmd(Token.NoToken, majorInTempLhss, majorInExprs);
        
          var minorInTempLhss = dupTempInVars.Select(x => (AssignLhs)new SimpleAssignLhs(x.Item2.tok, Expr.Ident(x.Item2))).ToList();
          var minorInExprs = callCmd.Ins.Select(_minorizer.VisitExpr).ToList();
          var minorInAssignCmd = new AssignCmd(Token.NoToken, minorInTempLhss, minorInExprs);
        
          var tempInAssignmentBBs = CreateNewIfBigBlockPair(majorInAssignCmd, minorInAssignCmd, majorContext, minorContext);

          var tempOutVars = callCmd.Outs.Select(e =>
          {
            var typedIdent = new TypedIdent(Token.NoToken, e.Name + "_temp_out" + FreshAnon(), GetTypeFromVarName(e.Name));
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

          var tempOutAssignmentBBs = CreateNewIfBigBlockPair(majorOutAssignCmd, minorOutAssignCmd, majorContext, minorContext);
        
          callCmd.Outs = FlattenVarList(dupTempOutVars).Select(Expr.Ident).ToList();
          callCmd.Ins = FlattenVarList(dupTempInVars)
            .Select(Expr.Ident)
            .Prepend(minorContext)
            .Prepend(majorContext)
            .ToList();
          var callBB = new BigBlock(
            Token.NoToken, 
            callCmd.ToString().TrimEnd(), 
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

          var newBB = new BigBlock(Token.NoToken, "call" + FreshAnon(), new List<Cmd>(), ifAnyContext, null);
          updatedBlocks.Add(newBB);
          break;
        }
        default:
          updatedBlocks.AddRange(CreateNewIfBigBlockPair(c, (Cmd)_minorizer.Visit(c.Clone()), majorContext,
            minorContext));
          break;
      }
    }

    return updatedBlocks;
  }

  private IEnumerable<BigBlock> CreateNewIfBigBlockPair(Cmd majorCmd, Cmd minorCmd, Expr majorContext, Expr minorContext)
  {
    var majorInternalBlock = new BigBlock(
      Token.NoToken,
      _majorPrefix + majorCmd.ToString().TrimEnd() + "_Then" + FreshAnon(),
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
      Token.NoToken,
      _majorPrefix + majorCmd.ToString().TrimEnd() + FreshAnon(),
      new List<Cmd>(),
      majorIf,
      null
    );

    var minorInternalBlock = new BigBlock(
      Token.NoToken,
      _minorPrefix + majorCmd.ToString().TrimEnd() + "_Then" + FreshAnon(),
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
      Token.NoToken,
      _minorPrefix + majorCmd.ToString().TrimEnd() + FreshAnon(),
      new List<Cmd>(),
      minorIf,
      null
    );

    return new List<BigBlock>() { majorBlock, minorBlock };
  }

  private Expr SolveExpr(Expr expr, Expr majorContext, Expr minorContext, MinorizeVisitor minorizer)
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
          var duplicatedBounds = DuplicateVariables(e.Dummies);
          var adaptedMinorizer = minorizer.AddTemporaryVariables(duplicatedBounds);
          return new ExistsExpr(e.tok,
            FlattenVarList(duplicatedBounds),
            SolveExpr(e.Body, majorContext, minorContext, adaptedMinorizer));
        }
        case ForallExpr f:
        {
          var duplicatedBounds = DuplicateVariables(f.Dummies);
          var adaptedMinorizer = minorizer.AddTemporaryVariables(duplicatedBounds);
          return new ForallExpr(f.tok,
            FlattenVarList(duplicatedBounds),
            SolveExpr(f.Body, majorContext, minorContext, adaptedMinorizer));
        }
        default:
          throw new ArgumentException();
      }
    }

    if (expr is QuantifierExpr q)
    {
      var duplicatedBounds = DuplicateVariables(q.Dummies);
      var adaptedMinorizer = minorizer.AddTemporaryVariables(duplicatedBounds);
      q.Dummies = FlattenVarList(duplicatedBounds);
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

  private int FreshAnon()
  {
    return _anon++;
  }

  public static List<Variable> FlattenVarList(List<(Variable, Variable)> varList)
  {
    return varList.SelectMany(tuple => new List<Variable> { tuple.Item1, tuple.Item2 })
      .ToList();
  }

  private List<(Variable, Variable)> AddLocalVars(List<Variable> variables)
  {
    var duplicatedVars = DuplicateVariables(variables);
    _localVariables.AddRange(duplicatedVars);
    return duplicatedVars;
  }

  private void SplitVariablesByContext(List<Variable> variables, out List<Variable> majorVars, out List<Variable> minorVars)
  {
    majorVars = variables.Where((item, index) => index % 2 == 0).ToList();
    minorVars = variables.Where((item, index) => index % 2 != 0).ToList();
  }

  private Type GetTypeFromVarName(String name)
  {
    return _localVariables.Select(x => x.Item1).First(x => x.Name.Equals(name)).TypedIdent.Type;
  }
}