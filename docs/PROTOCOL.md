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

Password is sent via JSON authentication command after connecting.

**WebSocket & TCP:**
After connecting, send auth command:
```json
{
  "Command": "auth",
  "Password": "your_password",
  "Identifier": 1
}
```

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

**Request:**
```json
{
  "Command": "auth",
  "Password": "your_password",
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
    "WorldTime": "2024-11-25T10:30:45.1234567Z"
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
    "Version": "1.0.88",
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
- **Version** (string): RCON mod version
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
- **Location** (string): Landblock coordinates (LOCString format)

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
4. If validation succeeds, connection is established but client is not yet marked authenticated
5. Client automatically sends HELLO command to validate authentication
6. If HELLO succeeds, client is authenticated and receives initial server state
7. All subsequent commands require authenticated connection

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

## Future Extensions

Planned for Phase 2:
- Broadcast messages to multiple clients
- Command scheduling
- Server event subscriptions
- More granular command access control
