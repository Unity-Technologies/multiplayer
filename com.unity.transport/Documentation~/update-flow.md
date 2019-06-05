# Understanding the Update flow

We call the driver's `ScheduleUpdate` method on every frame. This is so we can update the state of each connection we have active to make sure we read all data that we have received and finally produce events that the user can react to using `PopEvent` and `PopEventForConnection`.

The `Update` loop of the driver is really simple, it might look daunting at first glance but if you strip out all of the job system dependencies you will see we only do three things here:

![FlowchartUpdate](images/com.unity.transport.driver.png)

1. We start by calling our `InternalUpdate`, this call is where we clean up any stale connections, we clear our buffers and we finally check timeouts on our connections.
2. The second thing in the chain is running the `ReceiveJob` for reading and parsing the data from the socket.
3. Finally for each new message we receive on the socket we call a `AppendPacket` function that parses each packet received and either creates an event for it or discards it.

That's it, we clean up, we populate our buffers and we push new events. 

You could almost view the `NetworkDriver` as a Control System for the State Machine handling 
`NetworkConnection`.



[Back to table of contents](TableOfContents.md)