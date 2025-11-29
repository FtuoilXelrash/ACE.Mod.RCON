# RCON Protocol Specification

## Overview

The RCON protocol uses JSON for message encoding over both TCP (persistent connections) and WebSocket (HTTP) transports. All messages follow a request/response pattern with an identifier for correlation.

## Authentication Modes

The RCON server supports two authentication modes, configurable via `UseAceAuthentication` setting:

### Rust-style Authentication (UseAceAuthentication=false, default)

Password is embedded in the connection path or sent as the first message.

**WebSocket:**
```
ws://host:9005/password
```
The password is part of the URL path. Connection is authenticated immediately upon successful password validation.

**TCP:**
First message must be the plain-text password (not JSON). After server validates it, connection is authenticated for subsequent JSON commands.

### ACE-style Authentication (UseAceAuthentication=true)

Account credentials are validated against the ACE server's admin accounts via JSON command.

**WebSocket & TCP:**
After connecting, send auth command with account name and password:
```json
{
  "Command": "auth",
  "Name": "account_name",
  "Password": "account_password",
  "Identifier": 1
}
```

**Requirements:**
- Account must exist in the ACE database (via DatabaseManager.Authentication)
- Account must have admin access: AccessLevel >= 4 (Developer or Admin)
- Password must match using ACE's PasswordMatches() method (supports BCrypt and SHA512)
- Invalid credentials result in "error" status and immediate connection close
- Non-admin accounts are rejected with appropriate error message

Server responds with authentication status. All other commands must wait for successful authentication.

## Message Format

### Request

```json
{
  "Command": "command_name",
  "Password": "password",
  "Args": ["arg1", "arg2"],
  "Identifier": 1
}
```

**Fields:**
- **Command** (string, required): Command to execute
- **Password** (string, optional): Required for auth command only
- **Args** (array, optional): Command arguments
- **Identifier** (int, required): Unique ID to correlate with response

### Response

```json
{
  "Identifier": 1,
  "Status": "success",
  "Message": "User-friendly message",
  "Data": { "key": "value" },
  "Error": null,
  "Debug": false
}
```

**Fields:**
- **Identifier** (int): Echoed from request
- **Status** (string): "success", "error", "authenticated"
- **Message** (string): Human-readable response
- **Data** (object, optional): Structured data response
- **Error** (string, optional): Error details if Status is "error"
- **Debug** (bool): Set to Settings.DebugMode value

## Commands

### auth

Authenticate with the server.

**Rust-style Request:**
```json
{
  "Command": "auth",
  "Password": "your_password",
  "Identifier": 1
}
```

**ACE-style Request:**
```json
{
  "Command": "auth",
  "Name": "admin_account",
  "Password": "account_password",
  "Identifier": 1
}
```

**Success Response:**
```json
{
  "Identifier": 1,
  "Status": "authenticated",
  "Message": "Authentication successful",
  "Data": {
    "ServerName": "Asheron's Call",
    "Status": "Online",
    "CurrentPlayers": 5,
    "MaxPlayers": 255,
    "Uptime": "1d 3h 45m 12s",
    "WorldTime": "2024-11-25T10:30:45.1234567Z",
    "AceServerVersion": "1.72",
    "AceServerBuild": 4719,
    "AceDatabaseBaseVersion": 90001,
    "AceDatabasePatchVersion": 2022022
  },
  "Debug": false
}
```

**Error Response:**
```json
{
  "Identifier": 1,
  "Status": "error",
  "Message": "Invalid password",
  "Debug": false
}
```

**Note:** On successful authentication, server automatically fetches status data to populate client sidebar.

### config

Get client configuration and authentication mode. Allows clients to auto-detect server settings without authentication.

**Request:**
```json
{
  "Command": "config",
  "Identifier": 1
}
```

**Response:**
```json
{
  "Identifier": 1,
  "Status": "success",
  "Message": "Client configuration",
  "Data": {
    "RconEnabled": true,
    "WebRconEnabled": true,
    "MaxConnections": 10,
    "ConnectionTimeoutSeconds": 300,
    "EnableLogging": false,
    "DebugMode": false,
    "AutoRefreshPlayers": true,
    "MaxReconnectAttempts": 42,
    "ReconnectDelayMs": 15000,
    "UseAceAuthentication": false
  },
  "Debug": false
}
```

**Data Fields:**
- **RconEnabled** (bool): TCP RCON server status
- **WebRconEnabled** (bool): Web RCON (WebSocket) status
- **MaxConnections** (int): Maximum concurrent connections allowed
- **ConnectionTimeoutSeconds** (int): Idle connection timeout
- **EnableLogging** (bool): Server logging enabled
- **DebugMode** (bool): Raw response logging in server console
- **AutoRefreshPlayers** (bool): Auto-refresh player list on login/logoff
- **MaxReconnectAttempts** (int): Maximum reconnection attempts for clients
- **ReconnectDelayMs** (int): Delay between reconnection attempts (milliseconds)
- **UseAceAuthentication** (bool): Authentication mode (false=Rust-style URL auth, true=ACE-style JSON auth)

### hello

Get initial server state including player list. Automatically sent by web client after authentication to populate sidebar and player list.

**Request:**
```json
{
  "Command": "hello",
  "Identifier": 2
}
```

**Response:**
```json
{
  "Identifier": 2,
  "Status": "success",
  "Message": "Server hello",
  "Data": {
    "ServerName": "Asheron's Call",
    "Status": "Online",
    "CurrentPlayers": 5,
    "MaxPlayers": 255,
    "Uptime": "1d 3h 45m 12s",
    "WorldTime": "2024-11-25T10:30:45.1234567Z",
    "AceServerVersion": "1.72",
    "AceServerBuild": 4719,
    "AceDatabaseBaseVersion": 90001,
    "AceDatabasePatchVersion": 2022022,
    "OnlinePlayers": [
      {
        "Name": "PlayerName",
        "Guid": "0x123456789ABCDEF0",
        "Level": 180,
        "Race": "Aluvian",
        "Location": "AR: 001A 015E 001B"
      }
    ]
  },
  "Debug": false
}
```

**Data Fields:**
- **ServerName** (string): Server display name
- **Status** (string): "Online" or "Offline"
- **CurrentPlayers** (int): Number of online players
- **MaxPlayers** (int): Maximum concurrent players
- **Uptime** (string): Formatted as "XdYhZmSs"
- **WorldTime** (string): Current world time (UTC ISO 8601)
- **AceServerVersion** (string): ACE server version (e.g., "1.72")
- **AceServerBuild** (int): ACE server build number
- **AceDatabaseBaseVersion** (int): World database base version
- **AceDatabasePatchVersion** (int): World database patch version
- **OnlinePlayers** (array): Array of player objects with full details

### status

Get current server status for periodic polling. Lean response suitable for frequent updates (no ServerName since it doesn't change, no player list).

**Request:**
```json
{
  "Command": "status",
  "Identifier": 2
}
```

**Response:**
```json
{
  "Identifier": 2,
  "Status": "success",
  "Message": "Server status",
  "Data": {
    "Status": "Online",
    "CurrentPlayers": 5,
    "MaxPlayers": 255,
    "Uptime": "1d 3h 45m 12s",
    "WorldTime": "2024-11-25T10:30:45.1234567Z",
    "AceServerVersion": "1.72",
    "AceServerBuild": 4719,
    "AceDatabaseBaseVersion": 90001,
    "AceDatabasePatchVersion": 2022022
  },
  "Debug": false
}
```

**Data Fields:**
- **Status** (string): "Online" or "Offline"
- **CurrentPlayers** (int): Number of online players
- **MaxPlayers** (int): Maximum concurrent players
- **Uptime** (string): Formatted as "XdYhZmSs"
- **WorldTime** (string): Current world time (UTC ISO 8601)
- **AceServerVersion** (string): ACE server version
- **AceServerBuild** (int): ACE server build number
- **AceDatabaseBaseVersion** (int): World database base version
- **AceDatabasePatchVersion** (int): World database patch version

**Note:** STATUS is optimized for frequent polling and omits ServerName (static) and OnlinePlayers list (provided by dedicated HELLO or PLAYERS commands).

### players

Get list of online players with player count. Used for manual refresh or automatic updates on login/logoff events.

**Request:**
```json
{
  "Command": "players",
  "Identifier": 3
}
```

**Response:**
```json
{
  "Identifier": 3,
  "Status": "success",
  "Message": "Player list",
  "Data": {
    "CurrentPlayers": 5,
    "MaxPlayers": 255,
    "OnlinePlayers": [
      {
        "Name": "PlayerName",
        "Guid": "0x123456789ABCDEF0",
        "Level": 180,
        "Race": "Aluvian",
        "Location": "AR: 001A 015E 001B"
      }
    ]
  },
  "Debug": false
}
```

**Data Fields:**
- **CurrentPlayers** (int): Number of online players
- **MaxPlayers** (int): Maximum concurrent players
- **OnlinePlayers** (array): Array of player objects

**Player Object Fields:**
- **Name** (string): Character name
- **Guid** (string): Unique player GUID (hex format)
- **Level** (int): Character level
- **Race** (string): Heritage group/race
- **AccountName** (string): Account name associated with the character
- **Location** (string): Landblock coordinates (LOCString format)

### help

Display available ACE console commands. This is a passthrough to the ACE server's help command.

**Request:**
```json
{
  "Command": "help",
  "Identifier": 4
}
```

**Response:**
```json
{
  "Identifier": 4,
  "Status": "success",
  "Message": "Available commands:\nacecommands - Display available ACE commands\nlistplayers - List online players\n... [full list of ACE commands]",
  "Debug": false
}
```

## ACE Console Command Passthrough

All ACE server console commands are available through RCON command passthrough. Simply send the command name and any arguments as parameters. For a complete list of available ACE commands, see:

**[ACE Commands List - GitHub Wiki](https://github.com/ACEmulator/ACE/wiki/ACE-Commands)**

**Example Request:**
```json
{
  "Command": "world",
  "Args": ["broadcast", "Server maintenance in 5 minutes"],
  "Identifier": 10
}
```

**Example Response:**
```json
{
  "Identifier": 10,
  "Status": "success",
  "Message": "Broadcast: Server maintenance in 5 minutes",
  "Debug": false
}
```

## Error Handling

### Unauthenticated Request

All commands except `auth` require authentication.

**Response:**
```json
{
  "Identifier": 1,
  "Status": "error",
  "Message": "Authentication required. Send 'auth' command with password first.",
  "Debug": false
}
```

### Invalid JSON

**Response:**
```json
{
  "Identifier": -1,
  "Status": "error",
  "Message": "Invalid JSON",
  "Debug": false
}
```

### Unknown Command

**Response:**
```json
{
  "Identifier": 1,
  "Status": "error",
  "Message": "Unknown command: invalid_cmd. Type 'help' for available commands.",
  "Debug": false
}
```

### Server Error

**Response:**
```json
{
  "Identifier": 1,
  "Status": "error",
  "Message": "Error getting status: [exception message]",
  "Debug": false
}
```


## Transport Details

### TCP (Port 9004)

- Persistent connections
- Messages delimited by newlines
- One JSON object per line
- Connection stays open for multiple commands

**Message Format:**
```
{JSON message}\n
{JSON message}\n
```

### WebSocket (Port 9005)

**Rust-style Authentication (UseAceAuthentication=false):**
- HTTP upgrade to WebSocket at path containing password: `ws://host:9005/password`
- Password is extracted from URL path and validated immediately
- Connection is authenticated upon successful password validation

**ACE-style Authentication (UseAceAuthentication=true):**
- HTTP upgrade to WebSocket at `/rcon` endpoint: `ws://host:9005/rcon`
- Password must be sent via auth command after connecting
- Connection waits for auth command before accepting other commands

**General Details:**
- One message per WebSocket frame
- Binary frame type: Text (UTF-8 encoded JSON)
- Idle connections allowed indefinitely
- Client explicitly closes with WebSocketCloseFrame

## Authentication Details

### Rust-style Authentication Flow (UseAceAuthentication=false)

1. Client connects via WebSocket with password in URL path: `ws://host:9005/password`
2. Server validates password from URL path during HTTP upgrade
3. If validation fails, connection is rejected with WebSocket close code 1008 (PolicyViolation)
4. If validation succeeds, connection is established and marked authenticated
5. Client can immediately send commands without additional authentication
6. All commands require authenticated connection (non-authenticated connections rejected)

### ACE-style Authentication Flow (UseAceAuthentication=true)

1. Client connects via WebSocket to `ws://host:9005/rcon`
2. Client sends auth command with password: `{"Command": "auth", "Password": "xxx", "Identifier": 1}`
3. Server validates password
4. If validation fails, returns error response and closes connection
5. If validation succeeds, returns authenticated response with server status
6. All subsequent commands require authenticated connection

### Security Measures

- Password compared via constant-time comparison
- Invalid passwords close connection immediately (prevent brute force)
- One authenticated session per connection
- Authentication state is connection-specific
- No token/session ID needed (stateful per connection)
- Web client confirms sensitive operations (e.g., stop-now) before execution

## Response Status Values

| Status | Meaning |
|--------|---------|
| `success` | Command executed successfully, data available |
| `authenticated` | Authentication successful |
| `error` | Command failed or not authenticated |

## Debug Mode

When `Settings.DebugMode` is true, the `Debug` field is set to `true` in all responses. Web clients should display full JSON responses when this flag is set, allowing detailed inspection of the response structure and data.

## Server-Sent Broadcast Events

The server automatically sends broadcast events to all authenticated clients. Clients do not request these; they arrive unsolicited from the server.

### Player Login Event

Sent when a player enters the world.

**Format:**
```json
{
  "Identifier": -1,
  "Status": "broadcast",
  "Command": "player-login",
  "Message": "Player logged in",
  "Data": {
    "PlayerName": "CharacterName",
    "PlayerGuid": "0x123456789ABCDEF0",
    "Level": 180,
    "Location": "AR: 001A 015E 001B",
    "Count": 5,
    "WorldTime": "2024-11-25T10:30:45.1234567Z"
  },
  "Debug": false
}
```

**Fields:**
- **Identifier**: -1 (broadcast, not a response to a request)
- **Status**: "broadcast"
- **Command**: "player-login"
- **Count**: Updated total player count
- **WorldTime**: Server world time for uptime calculation

### Player Logoff Event

Sent when a player leaves the world.

**Format:**
```json
{
  "Identifier": -1,
  "Status": "broadcast",
  "Command": "player-logoff",
  "Message": "Player logged off",
  "Data": {
    "PlayerName": "CharacterName",
    "PlayerGuid": "0x123456789ABCDEF0",
    "Level": 180,
    "Location": "AR: 001A 015E 001B",
    "Count": 4,
    "WorldTime": "2024-11-25T10:30:45.1234567Z"
  },
  "Debug": false
}
```

### Console Log Broadcast

Sent when server logs a message (real-time log streaming).

**Format:**
```json
{
  "Identifier": -1,
  "Status": "broadcast",
  "Command": "console-log",
  "Message": "[CHAT] [PlayerName] Message content here",
  "Data": {
    "Timestamp": "2024-11-25T10:30:45.1234567Z",
    "Level": "INFO",
    "Type": "CHAT",
    "Content": "Message content here"
  },
  "Debug": false
}
```

**Fields:**
- **Level**: Log level (DEBUG, INFO, WARNING, ERROR)
- **Type**: Message tag (CHAT, AUDIT, SYSTEM, etc.) or null if no tag
- **Content**: Raw message content
- **Timestamp**: ISO 8601 UTC timestamp

## TCP Connection Examples

### Rust-style Authentication (TCP)

**Step 1: Connect to server**
```
telnet 127.0.0.1 9004
```

**Step 2: Send password as first message (plain text, no JSON)**
```
your_password
```

**Step 3: Send commands as JSON (one per line)**
```
{"Command": "status", "Identifier": 1}
```

**Step 4: Receive responses (JSON, one per line)**
```json
{
  "Identifier": 1,
  "Status": "success",
  "Message": "Server status",
  "Data": { ... }
}
```

### Complete TCP Flow Example

```
$ telnet 127.0.0.1 9004
Connected to 127.0.0.1.
Escape character is '^]'.
MySecurePassword
[server responds with queued commands or waits for input]
{"Command": "hello", "Identifier": 1}
{response with server state}
{"Command": "players", "Identifier": 2}
{response with player list}
```

### Message Delimiters

- TCP: Messages are delimited by newline character (\n)
- Each message must be valid JSON followed by a newline
- Empty lines are ignored
- Invalid JSON results in error response with Identifier -1

## Desktop Client Implementation Guide

### C# TCP Client Example

```csharp
using System;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

class RconClient
{
    private TcpClient client;
    private NetworkStream stream;
    private int nextId = 1;

    public void Connect(string host, int port, string password)
    {
        client = new TcpClient();
        client.Connect(host, port);
        stream = client.GetStream();

        // Send password as first message (plain text)
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password + "\n");
        stream.Write(passwordBytes, 0, passwordBytes.Length);
        stream.Flush();
    }

    public JsonElement SendCommand(string command, params string[] args)
    {
        var request = new
        {
            Command = command,
            Args = args,
            Identifier = nextId++
        };

        string json = JsonSerializer.Serialize(request) + "\n";
        byte[] data = Encoding.UTF8.GetBytes(json);
        stream.Write(data, 0, data.Length);
        stream.Flush();

        // Read response
        return ReadResponse();
    }

    private JsonElement ReadResponse()
    {
        // Read until newline
        var buffer = new StringBuilder();
        int read;
        while ((read = stream.ReadByte()) != -1)
        {
            if (read == '\n') break;
            buffer.Append((char)read);
        }

        return JsonDocument.Parse(buffer.ToString()).RootElement;
    }

    public void Disconnect()
    {
        stream?.Close();
        client?.Close();
    }
}
```

### Connection Management

**Reconnection Strategy:**
1. Attempt to connect with exponential backoff
2. Re-send password on reconnection
3. Maintain message queue during disconnection
4. Resume from last successful command ID
5. Re-sync state with HELLO command after reconnection

**Timeout Handling:**
- Set TCP socket timeout to avoid hanging indefinitely
- Implement heartbeat ping if needed (e.g., send STATUS periodically)
- Close connection if server doesn't respond within timeout

**Error Recovery:**
- Catch JSON parse errors gracefully
- Retry failed commands up to 3 times
- Log connection errors for debugging
- Notify user of persistent connection failures

