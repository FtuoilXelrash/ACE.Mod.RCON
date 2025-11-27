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

### status

Get current server status.

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
  "Message": "Server status retrieved",
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

**Data Fields:**
- **ServerName** (string): Server display name
- **Status** (string): "Online" or "Offline"
- **CurrentPlayers** (int): Number of online players
- **MaxPlayers** (int): Maximum concurrent players
- **Uptime** (string): Formatted as "XdYhZmSs"
- **WorldTime** (string): Current world time (UTC ISO 8601)

### players

Get list of online players.

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
  "Message": "Player list retrieved",
  "Data": {
    "players": [
      {
        "Name": "PlayerName",
        "Guid": "0x123456789ABCDEF0",
        "Level": 180,
        "Race": "Aluvian",
        "Location": "AR: 001A 015E 001B"
      }
    ],
    "count": 1
  },
  "Debug": false
}
```

**Player Object Fields:**
- **Name** (string): Character name
- **Guid** (string): Unique player GUID (hex)
- **Level** (int): Character level
- **Race** (string): Heritage group/race
- **Location** (string): Landblock coordinates (LOCString format)

### landblocks

Get information about loaded landblocks.

**Request:**
```json
{
  "Command": "landblocks",
  "Identifier": 4
}
```

**Response:**
```json
{
  "Identifier": 4,
  "Status": "success",
  "Message": "Landblock info retrieved",
  "Data": {
    "landblocks": [
      {
        "Id": "0x0001",
        "Status": "Active",
        "X": "00",
        "Y": "1e",
        "IsDungeon": false,
        "HasDungeon": true,
        "Players": 3,
        "Creatures": 15
      }
    ],
    "count": 1
  },
  "Debug": false
}
```

**Landblock Object Fields:**
- **Id** (string): Landblock ID (hex)
- **Status** (string): "Permaload", "Dormant", or "Active"
- **X** (string): X coordinate (hex)
- **Y** (string): Y coordinate (hex)
- **IsDungeon** (bool): Is this a dungeon landblock
- **HasDungeon** (bool): Does this landblock have a dungeon
- **Players** (int): Number of players in landblock
- **Creatures** (int): Number of creatures in landblock

### help

Display available commands.

**Request:**
```json
{
  "Command": "help",
  "Identifier": 5
}
```

**Response:**
```json
{
  "Identifier": 5,
  "Status": "success",
  "Message": "Available RCON Commands:\n  status    - Get server status information\n  players   - Get list of online players\n  landblocks - Get loaded landblock information\n  help      - Show this help message\n\nUse: {\"Command\": \"command_name\", \"Identifier\": 1}\nAuth: {\"Command\": \"auth\", \"Password\": \"your_password\", \"Identifier\": 1}",
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

## Command Passthrough

All ACE console commands are available via RCON using the command passthrough feature. The RCON server accepts ANY console command that works in the ACE server console.

**Generic Command Format:**
```json
{
  "Command": "command_name",
  "Args": ["arg1", "arg2"],
  "Identifier": 1
}
```

**Example: Execute "world broadcast"**
```json
{
  "Command": "world",
  "Args": ["broadcast", "Hello from RCON"],
  "Identifier": 1
}
```

**Response:**
```json
{
  "Identifier": 1,
  "Status": "success",
  "Message": "[Command output from ACE server]",
  "Type": "Generic",
  "Debug": false
}
```

The `Message` field contains the console output from executing the command via ACE's CommandManager. Any console output written during command execution is captured and returned in the response.

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

- Password compared via constant-time comparison
- One authenticated session per connection
- Authentication state is connection-specific
- No token/session ID needed (stateful per connection)

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
