// RUN: %parallel-boogie /securityverify "%s" > "%t"
// RUN: %diff "%s.expect" "%t"

procedure plusone(x: int) returns (y: int)
    ensures y == x+1; 
{
    y := x + 1;
}

procedure test(i: int)
    requires low(i);
{
    var x: int;
    call x := plusone(i);

    assert x == i + 1;
    assert low(x);
}