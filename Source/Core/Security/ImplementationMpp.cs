using System;
using System.Collections.Generic;
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

public class ImplementationMpp
{
  private List<(Variable, Variable)> _localVariables;
  private List<(Variable, Variable)> _inParams;
  private List<(Variable, Variable)> _outParams;

  public List<Variable> LocalVariables => Util.FlattenVarList(_localVariables);

  public List<Variable> InParams => Util.FlattenVarList(_inParams);

  public List<Variable> OutParams => Util.FlattenVarList(_outParams);

  public StmtList StructuredStmts { get; }

  public Implementation Implementation { get; }

  private MinorizeVisitor _minorizer;
  private int _anon = 0;

  public ImplementationMpp(Implementation implementation, Dictionary<string, (Variable, Variable)> globalVariableDict)
  {
    var minorizer = new MinorizeVisitor(globalVariableDict);
    _localVariables = Util.DuplicateVariables(implementation.LocVars, minorizer);
    _inParams = Util.CalculateInParams(implementation.InParams, minorizer);
    _outParams = Util.DuplicateVariables(implementation.OutParams, minorizer);

    _minorizer = minorizer.AddTemporaryVariables(_inParams.Concat(_outParams).Concat(_localVariables)
      .ToList());

    StructuredStmts = CalculateStructuredStmts(implementation.StructuredStmts);

    Implementation = new Implementation(
      implementation.tok,
      implementation.Name,
      implementation.TypeParameters,
      Util.FlattenVarList(_inParams),
      Util.FlattenVarList(_outParams),
      Util.FlattenVarList(_localVariables),
      StructuredStmts);
  }

  public StmtList CalculateStructuredStmts(StmtList structuredStmts)
  {
    if (structuredStmts == null)
    {
      return null;
    }

    var newBlocks = new List<BigBlock>();

    foreach (var bb in structuredStmts.BigBlocks)
    {
      bb.simpleCmds = DuplicateSimpleCommands(bb.simpleCmds);
      if (bb.ec is IfCmd originalIfCmd)
      {
        UpdateIfCmd(originalIfCmd, bb.simpleCmds);
      }
      else if (bb.ec is WhileCmd whileCmd)
      {
        bb.simpleCmds.Add(AssertLow(whileCmd.Guard));
        whileCmd.Invariants.ForEach(x => { x.Expr = Util.SolveExpr(x.Expr, _minorizer); });

        whileCmd.Body = CalculateStructuredStmts(whileCmd.Body);

      }
      else if (bb.ec is BreakCmd breakCmd)
      {
        breakCmd.BreakEnclosure = null;
      }
      
      newBlocks.Add(bb);
      

    }

    return new StmtList(newBlocks, structuredStmts.EndCurly);
  }

  private List<Cmd> DuplicateSimpleCommands(List<Cmd> simpleCmds)
  {
    var duplicatedSimpleCmds = new List<Cmd>();
    foreach (var c in simpleCmds)
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

          duplicatedSimpleCmds.AddRange(new List<Cmd> { assignCmd, minorAssignCmd });
          break;
        }
        case AssertCmd or AssumeCmd:
        {
          Cmd solvedCmd = c switch
          {
            AssertCmd a => new AssertCmd(a.tok, Util.SolveExpr(a.Expr, _minorizer)),
            AssumeCmd a => new AssumeCmd(a.tok, Util.SolveExpr(a.Expr, _minorizer)),
            _ => throw new cce.UnreachableException()
          };


          duplicatedSimpleCmds.Add(solvedCmd);
          break;
        }
        case CallCmd callCmd:
        {
          callCmd.Outs = callCmd.Outs.SelectMany(i => new List<IdentifierExpr>
            { i, (IdentifierExpr)_minorizer.VisitIdentifierExpr(i) }).ToList();
          callCmd.Ins = callCmd.Ins.SelectMany(i => new List<Expr>
            { i, _minorizer.VisitExpr(i) }).ToList();
          duplicatedSimpleCmds.Add(callCmd);
          break;
        }
        case CommentCmd:
          duplicatedSimpleCmds.Add(c);
          break;
        default:
          duplicatedSimpleCmds.AddRange(new List<Cmd> { c, (Cmd)_minorizer.Visit(c.Clone()) });
          break;
      }
    }

    return duplicatedSimpleCmds;
  }

  private void UpdateIfCmd(IfCmd ifCmd, ICollection<Cmd> simpleCmds)
  {
    if (!IsIfCmdEmpty(ifCmd))
    {
      simpleCmds.Add(AssertLow(ifCmd.Guard));
    }

    ifCmd.thn = CalculateStructuredStmts(ifCmd.thn);
    ifCmd.elseBlock = CalculateStructuredStmts(ifCmd.elseBlock);
    if (ifCmd.elseIf != null)
    {
      UpdateIfCmd(ifCmd.elseIf, simpleCmds);
    }
  }

  private bool IsIfCmdEmpty(IfCmd ifCmd)
  {
    return IsEmptyStmtList(ifCmd.thn) && IsEmptyStmtList(ifCmd.elseBlock) && ifCmd.elseIf == null;
  }

  private bool IsEmptyStmtList(StmtList stmtList)
  {
    return stmtList?.BigBlocks?.All(bb => bb.simpleCmds.Count == 0 && bb.ec == null && bb.tc == null) ?? true;
  }

  private AssertCmd AssertLow(Expr expr)
  {
    return new AssertCmd(expr.tok, Expr.Eq(expr, _minorizer.VisitExpr(expr)));
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

}