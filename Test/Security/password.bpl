procedure check_password(input: [int]int, pw: [int]int, n: int) returns (res: bool)
    requires n >= 0;
    requires low(n);
    requires (forall x: int :: 0 <= x && x < n ==> low(input[x]));
    requires low((forall x: int :: 0 <= x && x < n ==> input[x] == pw[x]));
    ensures res == (forall x: int :: 0 <= x && x < n ==> input[x] == pw[x]);
    ensures low(res);
{
    var i: int;
    var ok: bool;
    
    i := 0;
    ok := true;
    while (i < n)
        invariant 0 <= i && i <= n;
        invariant low(i) && low(n);
        invariant (forall x: int :: 0 <= x && x < i ==> low(input[x]));
        invariant ok == (forall x: int :: 0 <= x && x < i ==> input[x] == pw[x]);
    {
        ok := (pw[i] == input[i]) && ok;
        // assert low(ok);
        i := i + 1;
    }

    res := ok;
}