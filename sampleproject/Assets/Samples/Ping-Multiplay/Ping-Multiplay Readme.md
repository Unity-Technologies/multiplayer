# Ping-Multiplay Sample

## About
The *Ping-Multiplay* sample provides a sample implentation of various features to enable compatibility with Multiplay's cloud-hosted dedicated server solution.

Because it is based on the *Ping* sample:
* The main function of the client is to send pings to the server
* The main function of the server is to reply to pings

Additional functionality added to the Ping sample to enable Multiplay compatibility:
* SQP (Server Query Protocol) support
    * This allows Multiplay to monitor key metrics about the server process (ex: # of active players)
    * If a server does not respond to SQP queries, it is considered to be hung / in a bad state
* IP and port binding
* Config file reading

## Building and using the sample
The client and server can be built into standalone players, but can also both be run directly from the editor
* To run in the Editor, just load up the correct scene and enter play mode
    * Note: If you run the server in the editor, it will be initialized with the values set on the MultiplayPingServer GameObject

### Building the Multiplay Ping Server standalone executable
The server can be built using the Unity Editor's build window:
1. Open the Build Settings window
2. Ensure that the `MultiplayPingServer` scene is included and the only selected scene
3. Select a supported platform (Mac x64, Windows x64, Linux x64)
4. Ensure that the `Server Build` tickbox is ticked
5. Press the `Build` button

### Building the Multiplay Ping Client standalone executable
The client can be built using the Unity Editor's build window:
1. Open the Build Settings window
2. Ensure that the `MultiplayPingClient` scene is included and the only selected scene
3. Select a platform
4. Ensure that the `Server Build` tickbox is NOT ticked
5. Press the `Build` button

## Usage

### Server command-line arguments
|Argument|Effect|Default|
|---|---|---|
|`-nographics` and `-batchmode` together|Enable headless / command-line mode|Disabled|
|`-ip <value>`|IP address to use (bind to) |127.0.0.1|
|`-port <value>`|Port to use (bind to) for ping traffic between client and server|9000|
|`-query_port <value>`|Port to use for SQP (Server Query Protocol) traffic between the server and Multiplay|9010|
|`-config <value>`|Path to json config file used to initialize the server|Disabled|
|`-fps <value>`|Set the target FPS (FPS the server will *attempt* to run at)|120|
|`-timeout <value>`|Set the time (in seconds) the server must wait before automatically shutting down due to no activity (0 = infinite)|600 (10 minutes)|

**Special notes:**
* The **server** standalone **will not** show a GUI if launched normally, and should always be run in command-line mode
* The values for these can be set in many ways, but there is an order in which various settings will be overridden:
    * From lowest to highest priority:
        * Defaults
        * Config object passed on server construction
        * Config object loaded through `-config` arguments
        * All command-line arguments (`-ip`, `-port`, etc.) other than `-config`
* An example config file will be written to ExampleConfig.cfg on server startup if one doesn't already exist and the `-config` argument was not used
* While the config file includes `CurrentPlayers` as a value, it will be overridden at runtime by the number of connected ping clients

### Client command-line arguments
|Argument|Effect|Default|
|---|---|---|
|`-nographics` and `-batchmode` together|Enable headless / command-line mode|Disabled|
|`-fps <value>`|Set the target FPS (FPS the client will *attempt* to run at)|60|
|`-mm <value>`|Tell the client to use the provided **matchmaker URI** (`value`) and attempt to use matchmaking to connect to a server|Disabled|
|`-ping` / `-p`|Tell the client to connect to and ping a server|False|
|`-kill` / `-k`|Tell the client to connect to a server and send a remote shutdown signal.|False|
|`-endpoint <value>` / `-e <value>`|Tell the client which server to connect to.  `<value>` must be a valid IP address and port in the form `IPaddress:Port`.|None|
|`-t <value>`|The amount of time (ms) to spend pinging a server (requires `-p`)|5000 (5 seconds)|

**Special notes:**
* The **client** standalone player supports both GUI mode (if launched normally) and command-line mode
    * GUI mode will allow you to perform multiple operations against multiple servers repeatedly
    * Commandline mode will execute a specific task (specified by args) and then exit
* Commandline mode:
    * The client will print ping statistics and close after completing all tasks
    * The following arguments are *required* in commandline mode:
        * `-endpoint` or `-mm` (You need to specify how to connect to a server)
        * `-ping` and/or `-kill` (You must specicy what to do)
    * If you provide both `-ping` and `-kill` as arguments, the client will do the following:
        1. Connect to the server (specified by `-endpoint` or `-mm`)
        2. Ping for the duration set by `-t` (or use the default if not set)
        3. Send remote disconnect to the server
