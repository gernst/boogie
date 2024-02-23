// RUN: %parallel-boogie "%s" > "%t"
// RUN: %diff "%s.expect" "%t"
type {:linear} X;
const nil: X;

const max: int;
axiom 0 <= max;

var {:layer 0,1} l: [int]X;
var {:layer 0,2} status: [int]bool;

atomic action {:layer 2} atomic_Alloc({:linear} tid: One X) returns (r: int)
modifies status;
{
  assert tid->val != nil;
  if (*) {
    assume r == -1;
  } else {
    assume 0 <= r && r < max && status[r];
    status[r] := false;
  }
}

atomic action {:layer 2} atomic_Free({:linear} tid: One X, i: int)
modifies status;
{ assert tid->val != nil; status[i] := true; }

yield procedure {:layer 1} Alloc({:linear} tid: One X) returns (r: int)
refines atomic_Alloc;
{
  var i: int;
  var b: bool;

  i := 0;
  r := -1;
  while (i < max)
  invariant {:yields} true;
  invariant {:layer 1} 0 <= i;
  {
    call acquire(tid, i);
    call b := Read(tid, i);
    if (b) {
      call Write(tid, i, false);
      call release(tid, i);
      r := i;
      break;
    }
    call release(tid, i);
    i := i + 1;
  }
}

yield procedure {:layer 1} Free({:linear} tid: One X, i: int)
refines atomic_Free;
{
  call acquire(tid, i);
  call Write(tid, i, true);
  call release(tid, i);
}

right action {:layer 1} atomic_acquire({:linear} tid: One X, i: int)
modifies l;
{ assert tid->val != nil; assume l[i] == nil; l[i] := tid->val; }

left action {:layer 1} atomic_release({:linear} tid: One X, i: int)
modifies l;
{ assert tid->val != nil; assert l[i] == tid->val; l[i] := nil; }

both action {:layer 1} atomic_Read({:linear} tid: One X, i: int) returns (val: bool)
{ assert tid->val != nil; assert l[i] == tid->val; val := status[i]; }

both action {:layer 1} atomic_Write({:linear} tid: One X, i: int, val: bool)
modifies status;
{ assert tid->val != nil; assert l[i] == tid->val; status[i] := val; }

yield procedure {:layer 0} acquire({:linear} tid: One X, i: int);
refines atomic_acquire;

yield procedure {:layer 0} release({:linear} tid: One X, i: int);
refines atomic_release;

yield procedure {:layer 0} Read({:linear} tid: One X, i: int) returns (val: bool);
refines atomic_Read;

yield procedure {:layer 0} Write({:linear} tid: One X, i: int, val: bool);
refines atomic_Write;
