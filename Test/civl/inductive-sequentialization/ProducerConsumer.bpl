// RUN: %parallel-boogie "%s" > "%t"
// RUN: %diff "%s.expect" "%t"

// FIFO channel
datatype Channel { Channel(C: [int]int, head: int, tail: int) }

// id of a FIFO channel
type ChannelId;

// permission for sending to or receiving from a channel
datatype ChannelHandle { Send(cid: ChannelId), Receive(cid: ChannelId) }

function {:inline} BothHandles(cid: ChannelId): Set ChannelHandle {
  Set_Add(Set_Singleton(Send(cid)), Receive(cid))
}

// pool of FIFO channels
var {:layer 0,3} channels: [ChannelId]Channel;

////////////////////////////////////////////////////////////////////////////////

atomic action {:layer 2} MAIN (cid: ChannelId, {:linear_in} handles: Set ChannelHandle)
refines MAIN' using INV;
creates PRODUCER, CONSUMER;
eliminates CONSUMER using CONSUMER';
{
  assert handles == BothHandles(cid);
  assert channels[cid]->head == channels[cid]->tail;
  call create_async(PRODUCER(1, One(Send(cid))));
  call create_async(CONSUMER(1, One(Receive(cid))));
}

atomic action {:layer 3} MAIN' (cid: ChannelId, {:linear_in} handles: Set ChannelHandle)
modifies channels;
{
  var channel: Channel;

  assert handles == BothHandles(cid);
  assert channels[cid]->head == channels[cid]->tail;
  assume channel->head == channel->tail;
  channels[cid] := channel;
}

action {:layer 2}
INV (cid: ChannelId, {:linear_in} handles: Set ChannelHandle)
creates PRODUCER, CONSUMER;
modifies channels;
{
  var {:pool "INV1"} c: int;
  var {:pool "INV2"} channel: Channel;
  var C: [int]int;
  var head, tail: int;

  assert handles == BothHandles(cid);
  assert channels[cid]->head == channels[cid]->tail;

  C := channel->C;
  head := channel->head;
  tail := channel->tail;
  assume {:add_to_pool "INV1", c} 0 < c;
  if (*) {
    assume head == tail;
    call create_async(PRODUCER(c, One(Send(cid))));
    call create_async(CONSUMER(c, One(Receive(cid))));
    call set_choice(PRODUCER(c, One(Send(cid))));
  } else if (*) {
    assume tail == head + 1 && C[head] == 0;
    call create_async(CONSUMER(c, One(Receive(cid))));
    call set_choice(CONSUMER(c, One(Receive(cid))));
  } else if (*) {
    assume tail == head + 1 && C[head] == c;
    call create_async(PRODUCER(c+1, One(Send(cid))));
    call create_async(CONSUMER(c, One(Receive(cid))));
    call set_choice(CONSUMER(c, One(Receive(cid))));
  } else {
    assume head == tail;
  }
  channels[cid] := channel;
}

////////////////////////////////////////////////////////////////////////////////

async left action {:layer 2} PRODUCER (x: int, {:linear_in} send_handle: One ChannelHandle)
creates PRODUCER;
modifies channels;
{
  var channel: Channel;
  var C: [int]int;
  var head, tail: int;

  assert send_handle->val is Send;
  channel := channels[send_handle->val->cid];
  C := channel->C;
  head := channel->head;
  tail := channel->tail;
  if (*)
  {
    C[tail] := x;
    tail := tail + 1;
    call create_async(PRODUCER(x+1, send_handle));
  }
  else
  {
    C[tail] := 0;
    tail := tail + 1;
  }
  channels[send_handle->val->cid] := Channel(C, head, tail);
  assume {:add_to_pool "INV2", channels[send_handle->val->cid]} true;
}

async atomic action {:layer 2} CONSUMER (x: int, {:linear_in} receive_handle: One ChannelHandle)
creates CONSUMER;
modifies channels;
{
  var channel: Channel;
  var C: [int]int;
  var head, tail: int;
  var x': int;

  assert receive_handle->val is Receive;
  channel := channels[receive_handle->val->cid];
  C := channel->C;
  head := channel->head;
  tail := channel->tail;
  assert head < tail ==> C[head] == x || C[head] == 0;  // assertion to discharge

  assume head < tail;
  x' := C[head];
  head := head + 1;
  if (x' != 0)
  {
    call create_async(CONSUMER(x'+1, receive_handle));
  }
  channels[receive_handle->val->cid] := Channel(C, head, tail);
  assume {:add_to_pool "INV2", channels[receive_handle->val->cid]} true;
}

////////////////////////////////////////////////////////////////////////////////

action {:layer 2} CONSUMER' (x:int, {:linear_in} receive_handle: One ChannelHandle)
creates CONSUMER;
modifies channels;
{
  var channel: Channel;
  var head, tail: int;

  channel := channels[receive_handle->val->cid];
  head := channel->head;
  tail := channel->tail;
  assert head < tail;
  call CONSUMER(x, receive_handle);
}

////////////////////////////////////////////////////////////////////////////////

yield procedure {:layer 1}
main (cid: ChannelId, {:linear_in} handles: Set ChannelHandle)
refines MAIN;
{
  var {:linear} handles': Set ChannelHandle;
  var {:linear} send_handle, receive_handle: One ChannelHandle;

  handles' := handles;
  call send_handle := One_Get(handles', Send(cid));
  call receive_handle := One_Get(handles', Receive(cid));
  async call producer(1, send_handle);
  async call consumer(1, receive_handle);
}

yield procedure {:layer 1}
producer (x:int, {:linear_in} send_handle: One ChannelHandle)
refines PRODUCER;
{
  if (*)
  {
    call send(x, send_handle);
    async call producer(x+1, send_handle);
  }
  else
  {
    call send(0, send_handle);
  }
}

yield procedure {:layer 1}
consumer (x:int, {:linear_in} receive_handle: One ChannelHandle)
refines CONSUMER;
{
  var x': int;

  call x' := receive(receive_handle);
  if (x' != 0)
  {
    assert {:layer 1} x' == x; // low-level assertion to discharge
    async call consumer(x'+1, receive_handle);
  }
}

////////////////////////////////////////////////////////////////////////////////

atomic action {:layer 1} SEND (m: int, {:linear} send_handle: One ChannelHandle)
modifies channels;
{
  var channel: Channel;
  var C: [int]int;
  var head, tail: int;

  assert send_handle->val is Send;
  channel := channels[send_handle->val->cid];
  C := channel->C;
  head := channel->head;
  tail := channel->tail;
  C[tail] := m;
  tail := tail + 1;
  channels[send_handle->val->cid] := Channel(C, head, tail);
}

atomic action {:layer 1} RECEIVE ({:linear} receive_handle: One ChannelHandle) returns (m:int)
modifies channels;
{
  var channel: Channel;
  var C: [int]int;
  var head, tail: int;

  assert receive_handle->val is Receive;
  channel := channels[receive_handle->val->cid];
  C := channel->C;
  head := channel->head;
  tail := channel->tail;
  assume head < tail;
  m := C[head];
  head := head + 1;
  channels[receive_handle->val->cid] := Channel(C, head, tail);
}

yield procedure {:layer 0} send (m: int, {:linear} send_handle: One ChannelHandle);
refines SEND;

yield procedure {:layer 0} receive ({:linear} receive_handle: One ChannelHandle) returns (m: int);
refines RECEIVE;

