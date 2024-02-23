// RUN: %parallel-boogie "%s" > "%t"
// RUN: %diff "%s.expect" "%t"

var {:layer 0,2} x: int;

yield procedure {:layer 0} IncX();
refines AtomicIncX;

both action {:layer 1} AtomicIncX()
modifies x;
{ x := x + 1; }

yield procedure {:layer 1} SlowAdd(n: int)
refines AtomicSlowAdd;
requires {:layer 1} n >= 0;
{
    var i: int;
    var {:layer 1} oldx: int;

    call {:layer 1} oldx := Copy(x);
    i := 0;
    while (i < n)
    invariant {:layer 1} i <= n;
    invariant {:layer 1} x == oldx + i;
    {
        call IncX();
        i := i + 1;
    }

    assert {:layer 1} i == n;
}

both action {:layer 2} AtomicSlowAdd(n: int)
modifies x;
{ x := x + n; }
