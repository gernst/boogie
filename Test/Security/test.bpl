function {:relational} foo(n: int): bool;

axiom (forall n: int :: {foo(n)}
    foo(n) == low(n));

procedure test(x: int)
    requires low(x + 2);
{
    var y: int;

    y := 0;

    if(x == 0) {
        y := 0;
    }

    assert foo(x + 1);

    assert low(y);
}