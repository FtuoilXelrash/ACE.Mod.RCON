# ACE.Mod.RCON

RCON (Remote Console) mod for Asheron's Call Emulator (ACE) server. Provides remote server administration and monitoring through a web-based interface with real-time console streaming and player event monitoring.

## Features

**Phase 1-5 - Current Implementation:**
- ✅ TCP RCON server on port 9004 (configurable via RconPort)
- ✅ WebSocket HTTP server on port 9005 (configurable via WebRconPort, independently controllable)
- ✅ Web-based RCON client interface with responsive design
- ✅ Auto-detecting authentication modes (server-driven, no user dropdown):
  - Rust-style: Password-only authentication (RCON Password Authentication)
  - ACE-style: Username and password authentication (ACE Login/Password Authentication)
- ✅ Command passthrough to ACE CommandManager (execute ANY console command)
- ✅ Server status monitoring (uptime, player count, ACE version, database versions)
- ✅ Online player list with character details (GUID, level, location)
- ✅ Loaded landblock information
- ✅ Real-time console interface with server log streaming
- ✅ Player event detection (login/logoff with auto-refresh and real-time uptime updates)
- ✅ Customizable console message colors (tag-based and log-level coloring)
- ✅ Configuration tab for runtime settings management
- ✅ Debug mode for detailed response inspection and server logging
- ✅ Configurable reconnection settings (attempts and delay)
- ✅ Auto-refresh persistence via browser local storage
- ✅ Security: Invalid password keeps user on login page, no console access
- ✅ Stop Server Now button with confirmation dialog
- ✅ Quick command buttons for common operations
- ✅ Command and message history with dropdown selectors
- ✅ Tab-specific sidebars for Console, Players, and Configuration tabs
- ✅ Responsive input layout matching console window width

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
  "WebRconEnabled": true,
  "WebRconPort": 9005,
  "RconPassword": "your_secure_password",
  "UseAceAuthentication": false,
  "MaxConnections": 10,
  "ConnectionTimeoutSeconds": 300,
  "EnableLogging": false,
  "DebugMode": false,
  "AutoRefreshPlayers": true,
  "MaxReconnectAttempts": 42,
  "ReconnectDelayMs": 15000
}
```

**Settings Description:**
- **RconEnabled**: Enable/disable TCP RCON server (port 9004)
- **RconPort**: TCP port for RCON server (default: 9004)
- **WebRconEnabled**: Enable/disable Web RCON interface (WebSocket) - can be disabled independently from TCP RCON
- **WebRconPort**: WebSocket HTTP server port (default: 9005)
- **RconPassword**: Password for authentication (change this!)
- **UseAceAuthentication**: Use ACE-style account-based authentication instead of Rust-style password-only auth (default: false)
  - `false` (Rust-style): RCON password in WebSocket URL path (e.g., `ws://host:9005/password`) or first line of TCP connection
  - `true` (ACE-style): Login with ACE account credentials via JSON `{"Command": "auth", "Name": "accountname", "Password": "password"}`. Only accounts with Developer/Admin access (AccessLevel >= 4) are allowed
- **MaxConnections**: Maximum concurrent RCON connections (default: 10)
- **ConnectionTimeoutSeconds**: Idle connection timeout in seconds (default: 300/5 minutes)
- **EnableLogging**: Verbose logging of RCON operations to server console
- **DebugMode**: Show full JSON responses and Data objects in web client
- **AutoRefreshPlayers**: Automatically refresh player list on login/logoff events
- **MaxReconnectAttempts**: Maximum reconnection attempts for web client (default: 42 = ~10.5 minutes at 15-second intervals)
- **ReconnectDelayMs**: Delay between reconnection attempts in milliseconds (default: 15000)

### Accessing the Web Client

**Rust-style Authentication (default, UseAceAuthentication=false):**
1. Open your browser to: `http://127.0.0.1:9005/` (local) or `http://<server-ip>:9005/` (remote)
2. The web client automatically authenticates using the password in the URL path
3. If no saved password, the browser will prompt or you can manually connect to `ws://host:9005/your_password`

**ACE-style Authentication (UseAceAuthentication=true):**
1. Open your browser to: `http://127.0.0.1:9005/` (local) or `http://<server-ip>:9005/` (remote)
2. A login landing page will appear with Account Name and Password fields
3. Enter your ACE account name and password (account must have Developer or Admin access level)
4. Web client connects and sends authentication via JSON packet
5. Only accounts with AccessLevel >= 4 (Developer or Admin) are allowed access
6. Invalid credentials keep user on login page with error message

**Note:** Both TCP RCON (port 9004) and Web RCON (port 9005) accept connections from **all network interfaces** - accessible locally via 127.0.0.1 and remotely via any IP address the server is bound to. No admin privileges required.

### Server Console Commands

Reload settings without restarting:
```
rcon reload
```

## Available Commands

All ACE console commands are available via RCON passthrough. The RCON server accepts ANY console command that works in the ACE server console and returns the command output.

**Protocol Commands:**
- `config` - Get client configuration and auth mode
- `hello` - Get initial server state (status, version, player list, database info) on authentication
- `status` - Get lean server status for periodic polling (no player list)
- `players` - Get current player list and count

**Common Examples:**
- `acecommands` - Display available ACE commands
- `listplayers` - List online players
- `population` - Show player population info
- `help` - Display available commands
- Any other ACE console command (e.g., `world broadcast "message"`, `portal create`, etc.)

**Command Passthrough:**
The RCON implementation uses Rust RCON protocol format but with ACE CommandManager for command execution. Any command that can be executed in the ACE server console can be executed via RCON.

## Web Client Features

### Console Tab
- Real-time server console log streaming
- Server-side log broadcasting via custom log4net appender
- Message color coding by type:
  - **Tag-based colors** (takes priority):
    - `[CHAT]` messages - Green (#00ff00)
    - `[AUDIT]` messages - Yellow (#ffff00)
    - `[SYSTEM]` messages - Magenta (#ff00ff)
  - **Log-level colors** (fallback):
    - DEBUG - Gray (#888888)
    - INFO - Blue (#2196f3)
    - WARNING - Orange (#ff9800)
    - ERROR - Red (#f44336)
- Auto-scroll to new messages
- Customizable colors via color picker (saves to browser local storage)

### Players Tab
- Online player list with details (name, level, race, location)
- Player count display
- Manual refresh button
- Auto-refresh on player login/logoff (configurable)
- Auto-refresh preference persists across page reloads

### Configuration Tab
- **Console Colors**: Customize message colors with color pickers
- **Server Settings** (read-only display):
  - RCON Enabled (TCP server status)
  - Max Connections
  - Connection Timeout
  - Enable Logging
  - Debug Mode
  - Auto Refresh Players
  - Max Reconnect Attempts
  - Reconnect Delay
- **Web RCON Control**: Toggle Web RCON on/off independently from TCP RCON
- Requires server restart for most changes to take effect

### Server Info Sidebar
- Current server status
- Online player count
- Server uptime (days, hours, minutes, seconds) - updates on player login/logoff
- ACE Server version and build number
- World Database Base and Patch versions
- Quick command buttons (ACE Commands, List Players, Population, Status, Hello)

## Building

```bash
dotnet build                 # Development build
dotnet build -c Release      # Release build
```

## Documentation

- **[PROTOCOL.md](docs/PROTOCOL.md)** - JSON protocol and API details

## Architecture

### Real-Time Log Streaming
- **Server-side**: Custom log4net appender (RconLogAppender) intercepts all ACE logs in real-time
- **Broadcasting**: RconLogBroadcaster maintains list of connected WebSocket clients and broadcasts logs to all authenticated connections
- **Security**: Only authenticated clients receive log messages (one-way after auth)
- **TCP RCON**: TCP server can operate independently of web client
- **Web RCON**: WebSocket server can be disabled via WebRconEnabled setting

### Player Event Detection
- **Login Detection**: Harmony patch on `Player.PlayerEnterWorld()`
- **Logoff Detection**: Harmony patch on `PlayerManager.SwitchPlayerFromOnlineToOffline()`
- **Broadcasting**: Player events broadcast to all connected clients with:
  - Updated player count
  - Player name, GUID, level, location
  - Current world time for uptime calculation
- **Auto-refresh**: Web client can automatically refresh player list on these events
- **Real-time uptime**: Sidebar uptime updates whenever a player logs in/off

### Connection Management
- **Authentication**: Single password-based auth per connection (matches RCON/WebRcon password)
- **Multiple Clients**: Supports up to MaxConnections concurrent connections
- **Timeout**: Idle connections closed after ConnectionTimeoutSeconds
- **Reconnection**: Web client automatically reconnects with configurable attempts and delay
- **Status Tracking**: Clients shown connection status and player counts in real-time

## Development & Extension

### Adding New Commands
Edit `RconProtocol.cs` HandleCommandAsync() method to add new commands:
```csharp
case "newcommand" => HandleNewCommand(request),
```

### Log Broadcasting
Logs are automatically broadcast via RconLogBroadcaster when:
1. Server logs message via ModManager.Log() or log4net
2. RconLogAppender intercepts it
3. Broadcast to all authenticated WebSocket clients

### Player Events
Player events (login/logoff) are automatically broadcast when:
1. Harmony patches fire on player enter/logoff
2. Event data collected and sent to RconLogBroadcaster
3. Broadcast to all authenticated WebSocket clients with updated counts

## Troubleshooting

**Web client won't connect (local):**
- Verify WebRconEnabled = true in Settings.json
- Check RconHttpServer is running (port 9005)
- Check firewall allows port 9005
- Verify browser can reach http://127.0.0.1:9005/

**Web client won't connect (remote/external IP):**
- Verify WebRconEnabled = true in Settings.json
- Check firewall allows incoming connections on port 9005 (and port 9004 for TCP RCON)
- Verify remote host can reach the server IP on those ports (use `telnet <server-ip> 9005` to test)
- Both servers bind to all network interfaces (0.0.0.0) - no special configuration needed
- No admin privileges required on the server

**Player counts not updating:**
- Check EnableLogging = true to see debug output
- Verify AutoRefreshPlayers = true in settings
- Check player login/logoff is being detected in server logs

**Console colors not matching server:**
- Verify message tags are in format [TAG] at start of message
- Check custom color settings in Configuration tab
- Colors are stored in browser localStorage

**Connection drops frequently:**
- Increase MaxReconnectAttempts or ReconnectDelayMs if network is unreliable
- Check ConnectionTimeoutSeconds isn't too low
- Monitor server logs for connection errors
