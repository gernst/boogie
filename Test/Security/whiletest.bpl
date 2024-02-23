// RUN: %parallel-boogie /securityverify "%s" > "%t"
// RUN: %diff "%s.expect" "%t"

procedure test(x: int)
  requires x > 0;
  requires low(x);
{
    var i: int;

    assume low(i);
    i := 0;
    while (i < x)
        invariant i >= 0;
        invariant i <= x;
    {
        i := i + 1;
    }
    assert i == x;
    assert low(i);
}