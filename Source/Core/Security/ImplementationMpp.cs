using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Boogie {

  public class ImplementationMpp {
    private List<(Variable, Variable)> _localVariables;
    private List<(Variable, Variable)> _inParams;
    private List<(Variable, Variable)> _outParams;

    public List<Variable> LocalVariables => RelationalDuplicator.FlattenVarList(_localVariables);

    public List<Variable> InParams => RelationalDuplicator.FlattenVarList(_inParams);

    public List<Variable> OutParams => RelationalDuplicator.FlattenVarList(_outParams);

    public StmtList StructuredStmts { get; }

    public Implementation Implementation { get; }

    private MinorizeVisitor _minorizer;
    private int _anon = 0;
    private readonly List<string> _exclusions;
    private Program _program;

    public ImplementationMpp(Program program, Implementation implementation, Dictionary<string, (Variable, Variable)> globalVariableDict, List<string> exclusions) {
      _exclusions = exclusions;
      _program = program;
      var minorizer = new MinorizeVisitor(globalVariableDict);
      _localVariables = RelationalDuplicator.DuplicateVariables(implementation.LocVars, minorizer);
      _inParams = RelationalDuplicator.CalculateInParams(implementation.InParams, minorizer);
      _outParams = RelationalDuplicator.DuplicateVariables(implementation.OutParams, minorizer);

      _minorizer = minorizer.AddTemporaryVariables(_inParams.Concat(_outParams).Concat(_localVariables)
        .ToList());

      StructuredStmts = CalculateStructuredStmts(implementation.StructuredStmts);

      Implementation = new Implementation(
        implementation.tok,
        implementation.Name,
        implementation.TypeParameters,
        RelationalDuplicator.FlattenVarList(_inParams),
        RelationalDuplicator.FlattenVarList(_outParams),
        RelationalDuplicator.FlattenVarList(_localVariables),
        StructuredStmts);
    }

    public StmtList CalculateStructuredStmts(StmtList structuredStmts, bool isExcluded = false) {
      if (structuredStmts == null) {
        return null;
      }

      var newBlocks = new List<BigBlock>();

      foreach (var bb in structuredStmts.BigBlocks) {
        bb.simpleCmds = DuplicateSimpleCommands(bb.simpleCmds);
        if (bb.ec is IfCmd originalIfCmd) {
          UpdateIfCmd(originalIfCmd, bb.simpleCmds, isExcluded);
        } else if (bb.ec is WhileCmd whileCmd) {
          bb.simpleCmds.Add(AssertLow(whileCmd.Guard));
          whileCmd.Invariants.ForEach(x => { x.Expr = RelationalDuplicator.SolveExpr(_program, x.Expr, _minorizer); });

          whileCmd.Body = CalculateStructuredStmts(whileCmd.Body);

        } else if (bb.ec is BreakCmd breakCmd) {
          breakCmd.BreakEnclosure = null;
        }

        newBlocks.Add(bb);


      }

      return new StmtList(newBlocks, structuredStmts.EndCurly);
    }

    private List<Cmd> DuplicateSimpleCommands(List<Cmd> simpleCmds) {
      var duplicatedSimpleCmds = new List<Cmd>();
      foreach (var c in simpleCmds) {
        switch (c) {
          case AssignCmd assignCmd: {
              var lhss = new List<AssignLhs>();
              foreach (var lhs in assignCmd.Lhss) {
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
          case AssertCmd or AssumeCmd: {
              Cmd solvedCmd = c switch {
                AssertCmd a => new AssertCmd(a.tok, RelationalDuplicator.SolveExpr(_program, a.Expr, _minorizer)),
                AssumeCmd a => new AssumeCmd(a.tok, RelationalDuplicator.SolveExpr(_program, a.Expr, _minorizer)),
                _ => throw new cce.UnreachableException()
              };


              duplicatedSimpleCmds.Add(solvedCmd);
              break;
            }
          case CallCmd callCmd: {
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

    private void UpdateIfCmd(IfCmd ifCmd, ICollection<Cmd> simpleCmds, bool isExcluded = false) {
      isExcluded = IsExcluded(ifCmd.thn.Labels) || isExcluded;
      if (ifCmd.Guard != null && !isExcluded) {
        simpleCmds.Add(AssumeLow(ifCmd.Guard));
      }


      ifCmd.thn = CalculateStructuredStmts(ifCmd.thn, isExcluded);
      ifCmd.elseBlock = CalculateStructuredStmts(ifCmd.elseBlock, isExcluded);
      if (ifCmd.elseIf != null) {
        UpdateIfCmd(ifCmd.elseIf, simpleCmds, isExcluded);
      }
    }

    private AssumeCmd AssumeLow(Expr expr) {
      return new AssumeCmd(expr.tok, Expr.Eq(expr, _minorizer.VisitExpr(expr)));
    }

    private AssertCmd AssertLow(Expr expr) {
      var expr_ = RelationalDuplicator.SolveExpr(_program, expr, _minorizer);
      return new AssertCmd(expr.tok, Expr.Eq(expr, _minorizer.VisitExpr(expr)));
    }

    private int FreshAnon() {
      return _anon++;
    }

    private List<(Variable, Variable)> AddLocalVars(List<Variable> variables) {
      var duplicatedVars = RelationalDuplicator.DuplicateVariables(variables, _minorizer);
      _minorizer = _minorizer.AddTemporaryVariables(duplicatedVars);
      _localVariables.AddRange(duplicatedVars);
      return duplicatedVars;
    }

    private Type GetTypeFromVarName(String name) {
      return _localVariables.Select(x => x.Item1).First(x => x.Name.Equals(name)).TypedIdent.Type;
    }

    private bool IsExcluded(IEnumerable<string> labels) {
      return _exclusions.Exists(e => labels.ToList().Exists(l => l.Contains(e)));
    }

  }
}