// RUN: %parallel-boogie "%s" > "%t"
// RUN: %diff "%s.expect" "%t"

// This example shows how to use a bidirectional shared channel to communicate between
// two processes. The modeling of bidirectional channels is generic.
// Its usage is specifically illustrated here on a PingPong example
// containing two processes called Ping and Pong.

// A bidirectional channel is a pair of ordinary channels with two ends---left and right.
// The Ping and Pong processes share a channel pair with Ping holding its left end
// and Pong holding its right end.
datatype ChannelPair { ChannelPair(left: [int]int, right: [int]int) }

// The id type for indexing into the pool of bidirectional channels.
type ChannelId;

// The following global variables models all instances of a bidirectional channel indexed
// the ChannelId type. A single instance of PingPong will only use a single channel id.
var {:layer 0,3} channel: [ChannelId]ChannelPair;

// The id of a bidirectional channel can be split into two permissions---Left and Right.
// Left permission is used to receive from the left channel and send to the right channel.
// Right permission is used to receive from the right channel and send to the left channel.
datatype ChannelHandle { Left(cid: ChannelId), Right(cid: ChannelId) }

function {:inline} BothHandles(cid: ChannelId): Set ChannelHandle {
  Set_Add(Set_Singleton(Left(cid)), Right(cid))
}

function {:inline} EmptyChannel () : [int]int
{ (lambda m:int :: 0) }

////////////////////////////////////////////////////////////////////////////////

atomic action {:layer 3}
MAIN' (cid: ChannelId, {:linear_in} handles: Set ChannelHandle)
{
  assert handles == BothHandles(cid);
  assert channel[cid] == ChannelPair(EmptyChannel(), EmptyChannel());
}

action {:layer 2}
INV (cid: ChannelId, {:linear_in} handles: Set ChannelHandle)
creates PING, PONG;
modifies channel;
{
  var {:pool "INV"} c: int;

  assert handles == BothHandles(cid);
  assert channel[cid] == ChannelPair(EmptyChannel(), EmptyChannel());

  assume {:add_to_pool "INV", c, c+1} 0 < c;
  if (*) {
    channel[cid] := ChannelPair(EmptyChannel(), EmptyChannel()[c := 1]);
    call create_async(PONG(c, One(Right(cid))));
    call create_async(PING(c, One(Left(cid))));
    call set_choice(PONG(c, One(Right(cid))));
  } else if (*) {
    channel[cid] := ChannelPair(EmptyChannel(), EmptyChannel()[0 := 1]);
    call create_async(PONG(c, One(Right(cid))));
    call set_choice(PONG(c, One(Right(cid))));
  } else if (*) {
    channel[cid] := ChannelPair(EmptyChannel()[c := 1], EmptyChannel());
    call create_async(PONG(c+1, One(Right(cid))));
    call create_async(PING(c, One(Left(cid))));
    call set_choice(PING(c, One(Left(cid))));
  } else {
    channel[cid] := ChannelPair(EmptyChannel(), EmptyChannel());
  }
}

////////////////////////////////////////////////////////////////////////////////

action {:layer 2} PING' (x: int, {:linear_in} p: One ChannelHandle)
creates PING;
modifies channel;
{
  assert (exists {:pool "INV"} m:int :: channel[p->val->cid]->left[m] > 0);
  assert (forall m:int :: channel[p->val->cid]->right[m] == 0);
  call PING(x, p);
}

action {:layer 2} PONG' (y: int, {:linear_in} p: One ChannelHandle)
creates PONG;
modifies channel;
{
  assert (exists {:pool "INV"} m:int :: channel[p->val->cid]->right[m] > 0);
  assert (forall m:int :: channel[p->val->cid]->left[m] == 0);
  call PONG(y, p);
}

////////////////////////////////////////////////////////////////////////////////

atomic action {:layer 2}
MAIN (cid: ChannelId, {:linear_in} handles: Set ChannelHandle)
refines MAIN' using INV;
creates PING, PONG;
eliminates PING using PING', PONG using PONG';
modifies channel;
{
  assert handles == BothHandles(cid);
  assert channel[cid] == ChannelPair(EmptyChannel(), EmptyChannel());
  channel[cid] := ChannelPair(EmptyChannel(), EmptyChannel()[1 := 1]);
  call create_async(PING(1, One(Left(cid))));
  call create_async(PONG(1, One(Right(cid))));
}

async atomic action {:layer 2} PING (x: int, {:linear_in} p: One ChannelHandle)
creates PING;
modifies channel;
{
  var left_channel: [int]int;
  var right_channel: [int]int;

  left_channel := channel[p->val->cid]->left;
  right_channel := channel[p->val->cid]->right;

  assert x > 0;
  assert p->val is Left;
  assert (forall {:pool "INV"} m:int :: {:add_to_pool "INV", m} left_channel[m] > 0 ==> m == x); // assertion to discharge

  assume left_channel[x] > 0;
  left_channel[x] := left_channel[x] - 1;

  if (*)
  {
    right_channel[x+1] := right_channel[x+1] + 1;
    call create_async(PING(x+1, p));
  }
  else
  {
    right_channel[0] := right_channel[0] + 1;
  }
  channel[p->val->cid] := ChannelPair(left_channel, right_channel);
}

async atomic action {:layer 2} PONG (y: int, {:linear_in} p: One ChannelHandle)
creates PONG;
modifies channel;
{
  var left_channel: [int]int;
  var right_channel: [int]int;

  left_channel := channel[p->val->cid]->left;
  right_channel := channel[p->val->cid]->right;

  assert y > 0;
  assert p->val is Right;
  assert (forall {:pool "INV"} m:int :: {:add_to_pool "INV", m} right_channel[m] > 0 ==> m == y || m == 0); // assertion to discharge

  if (*)
  {
    assume right_channel[y] > 0;
    right_channel[y] := right_channel[y] - 1;
    left_channel[y] := left_channel[y] + 1;
    call create_async(PONG(y+1, p));
  }
  else
  {
    assume right_channel[0] > 0;
    right_channel[0] := right_channel[0] - 1;
  }
  channel[p->val->cid] := ChannelPair(left_channel, right_channel);
}

////////////////////////////////////////////////////////////////////////////////

yield procedure {:layer 1}
main (cid: ChannelId, {:linear_in} handles: Set ChannelHandle)
refines MAIN;
{
  var {:linear} handles': Set ChannelHandle;
  var {:linear} left: One ChannelHandle;
  var {:linear} right: One ChannelHandle;

  handles' := handles;
  call left := One_Get(handles', Left(cid));
  call right := One_Get(handles', Right(cid));
  call send(left->val, 1);
  async call ping(1, left);
  async call pong(1, right);
}

yield procedure {:layer 1}
ping (x: int, {:linear_in} p: One ChannelHandle)
refines PING;
{
  var x': int;

  call x' := receive(p->val);
  assert {:layer 1} x' == x; // low-level assertion to discharge
  if (*)
  {
    call send(p->val, x'+1);
    async call ping(x'+1, p);
  }
  else
  {
    call send(p->val, 0);
  }
}

yield procedure {:layer 1}
pong (y: int, {:linear_in} p: One ChannelHandle)
refines PONG;
{
  var y': int;

  call y' := receive(p->val);
  if (y' != 0)
  {
    assert {:layer 1} y' == y; // low-level assertion to discharge
    call send(p->val, y');
    async call pong(y'+1, p);
  }
}

////////////////////////////////////////////////////////////////////////////////
// Bidirectional channels

right action {:layer 1} RECEIVE (p: ChannelHandle) returns (m: int)
modifies channel;
{
  var left_channel: [int]int;
  var right_channel: [int]int;

  left_channel := channel[p->cid]->left;
  right_channel := channel[p->cid]->right;
  if (p is Left) {
    assume left_channel[m] > 0;
    left_channel[m] := left_channel[m] - 1;
  } else {
    assume right_channel[m] > 0;
    right_channel[m] := right_channel[m] - 1;
  }
  channel[p->cid] := ChannelPair(left_channel, right_channel);
}

left action {:layer 1} SEND (p: ChannelHandle, m: int)
modifies channel;
{
  var left_channel: [int]int;
  var right_channel: [int]int;

  left_channel := channel[p->cid]->left;
  right_channel := channel[p->cid]->right;
  if (p is Left) {
    right_channel[m] := right_channel[m] + 1;
  } else {
    left_channel[m] := left_channel[m] + 1;
  }
  channel[p->cid] := ChannelPair(left_channel, right_channel);
}

yield procedure {:layer 0} receive (p: ChannelHandle) returns (m: int);
refines RECEIVE;

yield procedure {:layer 0} send (p: ChannelHandle, m: int);
refines SEND;

