# ACE.Mod.RCON

RCON (Remote Console) mod for Asheron's Call Emulator (ACE) server. Provides remote server administration and monitoring through a web-based interface.

## Features

**Phase 1 - Current Implementation:**
- ✅ TCP RCON server on port 9004
- ✅ WebSocket HTTP server on port 9005
- ✅ Web-based RCON client interface
- ✅ Password-based authentication
- ✅ Server status monitoring (uptime, player count)
- ✅ Online player list with character details
- ✅ Loaded landblock information
- ✅ Real-time console interface
- ✅ Debug mode for detailed response inspection

## Quick Start

### Installation

1. Copy `RCON.dll` to your ACE mods directory
2. Add Settings.json to the mod directory (or let it auto-generate)
3. Restart the server

### Configuration

Edit `Settings.json`:

```json
{
  "RconEnabled": true,
  "RconPort": 9004,
  "RconPassword": "your_secure_password",
  "MaxConnections": 10,
  "ConnectionTimeoutSeconds": 300,
  "EnableLogging": false,
  "DebugMode": false
}
```

### Accessing the Web Client

1. Open your browser to: `http://127.0.0.1:9005/`
2. Enter your RCON password
3. Use available commands: `status`, `players`, `landblocks`, `help`

### Server Console Commands

Reload settings without restarting:
```
rcon reload
```

## Available Commands

| Command | Description |
|---------|-------------|
| `status` | Server status, uptime, player count |
| `players` | List online players with details |
| `landblocks` | Show loaded landblock information |
| `help` | Display available commands |

## Settings

- **RconEnabled**: Enable/disable RCON functionality
- **RconPort**: TCP port for RCON server (default: 9004)
- **RconPassword**: Password for authentication
- **MaxConnections**: Maximum concurrent connections (default: 10)
- **EnableLogging**: Verbose logging to server console
- **DebugMode**: Show full JSON responses in web client

## Building

```bash
dotnet build                 # Development build
dotnet build -c Release      # Release build
```

## Documentation

- **[PROTOCOL.md](PROTOCOL.md)** - JSON protocol and API details

## Web Client

The web client provides a real-time console interface for executing RCON commands. Features include:
- Command history navigation (arrow keys)
- Server info sidebar with uptime and player count
- Quick command buttons
- Color-coded message types
- Responsive design
