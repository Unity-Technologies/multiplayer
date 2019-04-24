# 2019-04-16
## New features
* Added network pipelines to enable processing of outgoing and incomming packets. The available pipeline stages are `ReliableSequencedPipelineStage` for reliable UDP messages and `SimulatorPipelineStage` for emulating network conditions such as high latency and packet loss. See [the pipeline documentation](com.unity.transport/Documentation/pipelines-usage.md) for more information.
* Added reading and writing of packed signed and unsigned integers to `DataStream`. These new methods use huffman encoding to reduce the size of transfered data for small numbers.
* Added a new sample asteroids game which we will be using to develop the new netcode.
## Changes
* Update to Unity.Entities preview 26
* Enable Burst compilation for most jobs
* Made it possible to get the remote endpoint for a connection
* Replacing EndPoint parsing with custom code to avoid having a dependency on System.Net
* Change the ping sample command-line parameters for server to -port and -query_port
* For matchmaking - use an Assignment object containing the ConnectionString, the Roster, and an AssignmentError string instead of just the ConnectionString.
## Fixes
* Fixed an issue with building iOS on Windows
* Fixed inconsistent error handling between platforms when the network buffer is full
## Upgrade guide
Unity 2019.1 is now required.

`BasicNetworkDriver` has been renamed to `GenericNetworkDriver` and a new `UdpNetworkDriver` helper class is also available.

System.Net EndPoints can no longer be used as addresses, use the new NetworkEndpoint struct instead.