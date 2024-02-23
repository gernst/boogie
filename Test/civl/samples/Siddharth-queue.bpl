// RUN: %parallel-boogie "%s" > "%t"
// RUN: %diff "%s.expect" "%t"
type Method;

type Invoc;

// Sequences of invocations
type SeqInvoc;
function Seq_append(s: SeqInvoc, o: Invoc) returns (t: SeqInvoc);

// Sets of invocations
type SetInvoc;
function Set_ofSeq(q: SeqInvoc) returns (s: SetInvoc);


var {:layer 1,2} lin: SeqInvoc;
var {:layer 1,2} vis: [Invoc]SetInvoc;

type Key;

// ---------- Primitives for manipulating logical/abstract state

pure procedure {:inline 1} intro_write_vis(vis: [Invoc]SetInvoc, n: Invoc, s: SetInvoc)
  returns (vis': [Invoc]SetInvoc)
{
  vis' := vis[n := s];
}

pure procedure {:inline 1} intro_writeLin(lin: SeqInvoc, n: Invoc) returns (lin': SeqInvoc)
{
  lin' := Seq_append(lin, n);
}

// ---------- Specification program:

atomic action {:layer 2} pop_atomic(this: Invoc) returns (k: Key)
  modifies lin, vis;
{
  var my_vis: SetInvoc;
  lin := Seq_append(lin, this);
  assume my_vis == Set_ofSeq(lin);
  // buggy transition relation computation due to assume after assignment to lin which
  // creates difference between lin and old(lin)
  vis[this] := my_vis;
}

// ---------- Implementation:

yield procedure {:layer 1} pop(this: Invoc) returns (k: Key)
refines pop_atomic;
{
  var {:layer 1} my_vis: SetInvoc;

  call {:layer 1} lin := intro_writeLin(lin, this);
  call {:layer 1} my_vis := Copy(Set_ofSeq(lin));
  call {:layer 1} vis := intro_write_vis(vis, this, my_vis);
  assert {:layer 1} my_vis == Set_ofSeq(lin);  // Despite this assertion passing
}
