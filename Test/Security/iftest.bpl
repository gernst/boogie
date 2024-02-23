// RUN: %parallel-boogie /securityverify "%s" > "%t"
// RUN: %diff "%s.expect" "%t"

procedure test(x: int)
  requires low(x);
{
  var y: int;

  y := 0;
  assert y == 0;
  if (x == 0) {
    y := 1;
  }
  
  assert low(y);
  assert x == 0 <==> y == 1;
 }