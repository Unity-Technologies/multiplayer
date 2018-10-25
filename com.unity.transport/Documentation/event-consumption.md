# Event consumption

There are currently 4 types of events supplied by the `NetworkDriver`

```c#
public enum Type
{
    Empty = 0,
    Data,
    Connect,
    Disconnect
}
```

As mentioned, there are a few subtle differences running the driver as a host or client. Mainly when it comes to consumption of events. 

Both your client and you server loop will want to consume the events that are produced by the `NetworkDriver`. And you do so by either calling `PopEvent` on each `NetworkConnection` similar to how we did before.

```c#
DataStreamReader strm;
NetworkEvent.Type cmd;
while ((cmd = m_Connection.PopEvent(driver, out strm)) != NetworkEvent.Type.Empty)
    ; // Handle Event
```

You can try calling the `PopEventForConnection` on the `NetworkDriver` as we did in the ServerBehaviour example:

```c#
DataStreamReader strm;
NetworkEvent.Type cmd;
while ((cmd = m_Driver.PopEventForConnection(m_Connections[i], out strm)) != NetworkEvent.Type.Empty)
    ; // Handle Event
```

There is no real difference between these calls, both calls will do the same thing. Its just how you want to phrase yourself when writing the code.

And finally to receive a new `NetworkConnection` on the Driver while Listening you can call `Accept`

```c#
NetworkConnection c;
while ((c = m_Driver.Accept()) != default(NetworkConnection))
    ; // Handle Connection Event.
```

| Event      | Description                                                  |
| ---------- | ------------------------------------------------------------ |
| Empty      | The `Empty` event signals that there are no more messages in our event queue to handle this frame. |
| Data       | The `Data` event signals that we have received data from a connected endpoint. |
| Connect    | The `Connect` event signals that a new connection has been established.<br> **Note**: this event is only available if the `NetworkDriver` is **not** in the `Listening` state. |
| Disconnect | The `Disconnect` event is received if;<br> 1. `Disconnect` packet was received (calling `NetworkConnection::Disconnect` will trigger this.)<br> 2. A *socket timeout* occurred.<br> 3. Maximum connect attempts on the `NetworkConnection` exceeded. <br> **Note:** That if you call `Disconnect` on your `NetworkConnection` this will **NOT** trigger an `Disconnect` event on your local `NetworkDriver`. |

Looking at this table we see that there are 2 things that stand out.

- The first thing is that the `Connect` event is only available if the `NetworkDriver` is **NOT** `Listening`  
  - In order to receive any `Connect` events on a `NetworkDriver` that is in the `Listening` state we need to call the special function `Accept` just as we did in the *Creating a Server* section in the [Creating a minimal client and server](workflow-client-server.md) workflow page.
- The second thing to notice is that if you call `Disconnect` on a `NetworkConnection` this will not trigger an event inside your own driver.



[Back to table of contents](TableOfContents.md)