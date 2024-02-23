﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Boogie
{
  public class MoverCheck
  {
    CivlTypeChecker civlTypeChecker;
    List<Declaration> decls;

    HashSet<Tuple<Action, Action>> commutativityCheckerCache;
    HashSet<Tuple<Action, Action>> gatePreservationCheckerCache;
    HashSet<Tuple<Action, Action>> failurePreservationCheckerCache;

    private MoverCheck(CivlTypeChecker civlTypeChecker, List<Declaration> decls)
    {
      this.civlTypeChecker = civlTypeChecker;
      this.decls = decls;
      this.commutativityCheckerCache = new HashSet<Tuple<Action, Action>>();
      this.gatePreservationCheckerCache = new HashSet<Tuple<Action, Action>>();
      this.failurePreservationCheckerCache = new HashSet<Tuple<Action, Action>>();
    }

    private ConcurrencyOptions Options => civlTypeChecker.Options;

    public static void AddCheckers(CivlTypeChecker civlTypeChecker, List<Declaration> decls)
    {
      MoverCheck moverChecking = new MoverCheck(civlTypeChecker, decls);

      // TODO: make enumeration of mover checks more efficient / elegant

      var regularMoverChecks =
        from first in civlTypeChecker.MoverActions
        from second in civlTypeChecker.MoverActions
        where first.LayerRange.OverlapsWith(second.LayerRange)
        where first.IsRightMover || second.IsLeftMover
        select new {first, second};

      foreach (var moverCheck in regularMoverChecks)
      {
        if (moverCheck.first.IsRightMover)
        {
          moverChecking.CreateRightMoverCheckers(moverCheck.first, moverCheck.second);
        }

        if (moverCheck.second.IsLeftMover)
        {
          moverChecking.CreateLeftMoverCheckers(moverCheck.first, moverCheck.second);
        }
      }

      var sequentializationMoverChecks =
        from sequentialization in civlTypeChecker.Sequentializations
        from leftMover in sequentialization.Abstractions
        from action in civlTypeChecker.MoverActions
        where action.LayerRange.Contains(sequentialization.Layer)
        let extraAssumption1 = sequentialization.GenerateMoverCheckAssumption(action, action.FirstImpl.InParams, leftMover, leftMover.SecondImpl.InParams)
        let extraAssumption2 = sequentialization.GenerateMoverCheckAssumption(action, action.SecondImpl.InParams, leftMover, leftMover.FirstImpl.InParams)
        select new {action, leftMover, extraAssumption1, extraAssumption2};

      /*
       * It is important that the mover checks required for sequentialization are the last ones
       * to be generated. Each of these mover checks may add an extra assumption. Since mover checks are
       * cached, if a mover check has already been generated then one generated here with the extra
       * assumption will get dropped. As a result, we preserve overall soundness.
       */
      foreach (var moverCheck in sequentializationMoverChecks)
      {
        moverChecking.CreateCommutativityChecker(moverCheck.action, moverCheck.leftMover, moverCheck.extraAssumption1);
        moverChecking.CreateGatePreservationChecker(moverCheck.leftMover, moverCheck.action, moverCheck.extraAssumption2);
        moverChecking.CreateFailurePreservationChecker(moverCheck.action, moverCheck.leftMover, moverCheck.extraAssumption1);
      }

      foreach (var action in civlTypeChecker.MoverActions.Where(a => a.IsLeftMover))
      {
        moverChecking.CreateCooperationChecker(action);
      }

      foreach (var action in civlTypeChecker.Sequentializations.SelectMany(sequentialization => sequentialization.Abstractions)
                 .Where(a => !a.IsLeftMover).Distinct())
      {
        moverChecking.CreateCooperationChecker(action);
      }
    }

    private IEnumerable<Requires> DisjointnessAndWellFormedRequires(IEnumerable<Variable> paramVars, HashSet<Variable> frame)
    {
      var availableVars = paramVars.Union(frame);
      return civlTypeChecker.linearTypeChecker.DisjointnessExprForEachDomain(availableVars)
        .Union(civlTypeChecker.linearTypeChecker.MapWellFormedExpressions(availableVars))
        .Select(expr => new Requires(false, expr));
    }

    private void AddChecker(string checkerName, List<Variable> inputs, List<Variable> outputs, List<Variable> locals,
      List<Requires> requires, List<Cmd> cmds)
    {
      checkerName = civlTypeChecker.AddNamePrefix(checkerName);
      var blocks = new List<Block> { BlockHelper.Block("init", cmds) };
      Procedure proc = DeclHelper.Procedure(checkerName, inputs, outputs, requires,
        civlTypeChecker.GlobalVariables.Select(v => Expr.Ident(v)).ToList(), new List<Ensures>());
      Implementation impl = DeclHelper.Implementation(proc, inputs, outputs, locals, blocks);
      this.decls.Add(impl);
      this.decls.Add(proc);
    }

    private void CreateRightMoverCheckers(Action rightMover, Action action)
    {
      CreateCommutativityChecker(rightMover, action);
      CreateGatePreservationChecker(action, rightMover);
    }

    private void CreateLeftMoverCheckers(Action action, Action leftMover)
    {
      CreateCommutativityChecker(action, leftMover);
      CreateGatePreservationChecker(leftMover, action);
      CreateFailurePreservationChecker(action, leftMover);
    }

    private CallCmd ActionCallCmd(Action action, DeclWithFormals paramProvider)
    {
      return CmdHelper.CallCmd(action.Impl.Proc, paramProvider.InParams, paramProvider.OutParams);
    }

    private void CreateCommutativityChecker(Action first, Action second, Expr extraAssumption = null)
    {
      if (first == second && first.FirstImpl.InParams.Count == 0 && first.FirstImpl.OutParams.Count == 0)
      {
        return;
      }

      if (first.TriviallyCommutesWith(second))
      {
        return;
      }

      if (!commutativityCheckerCache.Add(Tuple.Create(first, second)))
      {
        return;
      }

      string checkerName = $"CommutativityChecker_{first.Name}_{second.Name}";

      HashSet<Variable> frame = new HashSet<Variable>();
      frame.UnionWith(first.UsedGlobalVarsInGate);
      frame.UnionWith(first.UsedGlobalVarsInAction);
      frame.UnionWith(second.UsedGlobalVarsInGate);
      frame.UnionWith(second.UsedGlobalVarsInAction);

      var linearTypeChecker = civlTypeChecker.linearTypeChecker;
      List<Requires> requires =
        DisjointnessAndWellFormedRequires(
          first.FirstImpl.InParams.Union(second.SecondImpl.InParams)
            .Where(v => LinearTypeChecker.FindLinearKind(v) != LinearKind.LINEAR_OUT), frame).ToList();
      foreach (AssertCmd assertCmd in first.FirstGate.Union(second.SecondGate))
      {
        requires.Add(new Requires(false, assertCmd.Expr));
      }
      if (extraAssumption != null)
      {
        requires.Add(new Requires(false, extraAssumption));
      }

      var transitionRelation = TransitionRelationComputation.Commutativity(civlTypeChecker, second, first, frame);

      var secondInParamsFiltered =
        second.SecondImpl.InParams.Where(v => LinearTypeChecker.FindLinearKind(v) != LinearKind.LINEAR_IN);
      IEnumerable<Expr> linearityAssumes = linearTypeChecker.DisjointnessExprForEachDomain(first.FirstImpl.OutParams.Union(secondInParamsFiltered)
        .Union(frame)).Union(linearTypeChecker.DisjointnessExprForEachDomain(first.FirstImpl.OutParams.Union(second.SecondImpl.OutParams)
          .Union(frame)));
      // TODO: add further disjointness expressions?
      AssertCmd commutativityCheck = CmdHelper.AssertCmd(
        first.tok,
        Expr.Imp(Expr.And(linearityAssumes), transitionRelation),
        $"Commutativity check between {first.Name} and {second.Name} failed");

      List<Cmd> cmds = new List<Cmd>
      {
        ActionCallCmd(first, first.FirstImpl),
        ActionCallCmd(second, second.SecondImpl)
      };
      cmds.Add(commutativityCheck);

      List<Variable> inputs = first.FirstImpl.InParams.Union(second.SecondImpl.InParams).ToList();
      List<Variable> outputs = first.FirstImpl.OutParams.Union(second.SecondImpl.OutParams).ToList();

      AddChecker(checkerName, inputs, outputs, new List<Variable>(), requires, cmds);
    }

    private void CreateGatePreservationChecker(Action first, Action second, Expr extraAssumption = null)
    {
      if (!first.UsedGlobalVarsInGate.Intersect(second.ModifiedGlobalVars).Any())
      {
        return;
      }

      if (!gatePreservationCheckerCache.Add(Tuple.Create(first, second)))
      {
        return;
      }

      HashSet<Variable> frame = new HashSet<Variable>();
      frame.UnionWith(first.UsedGlobalVarsInGate);
      frame.UnionWith(second.UsedGlobalVarsInGate);
      frame.UnionWith(second.UsedGlobalVarsInAction);

      var linearTypeChecker = civlTypeChecker.linearTypeChecker;
      List<Requires> requires = 
        DisjointnessAndWellFormedRequires(
          first.FirstImpl.InParams.Union(second.SecondImpl.InParams)
            .Where(v => LinearTypeChecker.FindLinearKind(v) != LinearKind.LINEAR_OUT), frame).ToList();
      foreach (AssertCmd assertCmd in first.FirstGate.Union(second.SecondGate))
      {
        requires.Add(new Requires(false, assertCmd.Expr));
      }
      if (extraAssumption != null)
      {
        requires.Add(new Requires(false, extraAssumption));
      }

      string checkerName = $"GatePreservationChecker_{first.Name}_{second.Name}";

      List<Variable> inputs = first.FirstImpl.InParams.Union(second.SecondImpl.InParams).ToList();
      List<Variable> outputs = first.FirstImpl.OutParams.Union(second.SecondImpl.OutParams).ToList();

      List<Cmd> cmds = new List<Cmd> { ActionCallCmd(second, second.SecondImpl) };

      IEnumerable<Expr> linearityAssumes =
        linearTypeChecker.DisjointnessExprForEachDomain(first.FirstImpl.InParams.Union(second.SecondImpl.OutParams)
          .Union(frame));
      foreach (AssertCmd assertCmd in first.FirstGate)
      {
        cmds.Add(
          CmdHelper.AssertCmd(
            assertCmd.tok,
            Expr.Imp(Expr.And(linearityAssumes), assertCmd.Expr),
            $"Gate of {first.Name} not preserved by {second.Name}"
          )
        );
      }

      AddChecker(checkerName, inputs, outputs, new List<Variable>(), requires, cmds);
    }

    private void CreateFailurePreservationChecker(Action first, Action second, Expr extraAssumption = null)
    {
      if (!first.UsedGlobalVarsInGate.Intersect(second.ModifiedGlobalVars).Any())
      {
        return;
      }

      if (!failurePreservationCheckerCache.Add(Tuple.Create(first, second)))
      {
        return;
      }

      HashSet<Variable> frame = new HashSet<Variable>();
      frame.UnionWith(first.UsedGlobalVarsInGate);
      frame.UnionWith(second.UsedGlobalVarsInGate);
      frame.UnionWith(second.UsedGlobalVarsInAction);

      var linearTypeChecker = civlTypeChecker.linearTypeChecker;
      List<Requires> requires = 
        DisjointnessAndWellFormedRequires(
          first.FirstImpl.InParams.Union(second.SecondImpl.InParams)
            .Where(v => LinearTypeChecker.FindLinearKind(v) != LinearKind.LINEAR_OUT), frame).ToList();
      Expr firstNegatedGate = Expr.Not(Expr.And(first.FirstGate.Select(a => a.Expr)));
      firstNegatedGate.Type = Type.Bool; // necessary?
      requires.Add(new Requires(false, firstNegatedGate));
      foreach (AssertCmd assertCmd in second.SecondGate)
      {
        requires.Add(new Requires(false, assertCmd.Expr));
      }
      if (extraAssumption != null)
      {
        requires.Add(new Requires(false, extraAssumption));
      }

      IEnumerable<Expr> linearityAssumes =
        linearTypeChecker.DisjointnessExprForEachDomain(first.FirstImpl.InParams.Union(second.SecondImpl.OutParams)
          .Union(frame));
      AssertCmd gateFailureCheck = CmdHelper.AssertCmd(
        first.tok,
        Expr.Imp(Expr.And(linearityAssumes), firstNegatedGate),
        $"Gate failure of {first.Name} not preserved by {second.Name}");

      string checkerName = $"FailurePreservationChecker_{first.Name}_{second.Name}";

      List<Variable> inputs = first.FirstImpl.InParams.Union(second.SecondImpl.InParams).ToList();
      List<Variable> outputs = first.FirstImpl.OutParams.Union(second.SecondImpl.OutParams).ToList();
      var cmds = new List<Cmd>
      {
        ActionCallCmd(second, second.SecondImpl),
        gateFailureCheck
      };

      AddChecker(checkerName, inputs, outputs, new List<Variable>(), requires, cmds);
    }

    private void CreateCooperationChecker(Action action)
    {
      if (!action.HasAssumeCmd)
      {
        return;
      }

      string checkerName = $"CooperationChecker_{action.Name}";

      Implementation impl = action.Impl;
      HashSet<Variable> frame = new HashSet<Variable>();
      frame.UnionWith(action.UsedGlobalVarsInGate);
      frame.UnionWith(action.UsedGlobalVarsInAction);

      List<Requires> requires =
        DisjointnessAndWellFormedRequires(impl.InParams.Where(v => LinearTypeChecker.FindLinearKind(v) != LinearKind.LINEAR_OUT),
          frame).ToList();
      foreach (AssertCmd assertCmd in action.Gate)
      {
        requires.Add(new Requires(false, assertCmd.Expr));
      }

      AssertCmd cooperationCheck = CmdHelper.AssertCmd(
        action.tok,
        TransitionRelationComputation.Cooperation(civlTypeChecker, action, frame),
        $"Cooperation check for {action.Name} failed");

      AddChecker(checkerName, new List<Variable>(impl.InParams), new List<Variable>(),
        new List<Variable>(), requires, new List<Cmd> { cooperationCheck });
    }
  }
}