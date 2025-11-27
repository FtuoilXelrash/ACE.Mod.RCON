================================================================================
ACE.Mod.RCON - RCON (Remote Console) for Asheron's Call Emulator
================================================================================

WHAT IS IT?
-----------
A mod for Asheron's Call Emulator (ACE) that provides remote server administration
and monitoring through a web-based interface with real-time console streaming,
player event monitoring, and configurable settings.

QUICK START
-----------
1. Copy RCON.dll to your ACE mods directory
2. Restart your ACE server (Settings.json will auto-generate)
3. Open browser to: http://127.0.0.1:9005/ (local) or http://<server-ip>:9005/ (remote)
4. Enter your RCON password (default in Settings.json)
5. Use Console, Players, and Configuration tabs

PORTS
-----
- TCP RCON: Port 9004 (RconPort setting, RconEnabled to toggle)
- Web RCON: Port 9005 (WebRconPort setting, WebRconEnabled to toggle)
  Both can be controlled independently!

NETWORK ACCESS
--------------
✓ Both TCP RCON (9004) and Web RCON (9005) are accessible from ALL network interfaces
✓ Works locally via 127.0.0.1 AND remotely via any server IP address
✓ No admin privileges required on the server
✓ Firewall must allow inbound TCP on ports 9004 and 9005 for remote access

SETTINGS.JSON
-------------

Example Settings.json:

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

Settings Description:

RconEnabled (true/false)
  - Enable/disable TCP RCON server (port 9004)
  - Default: true

RconPort (number)
  - TCP port for RCON server
  - Default: 9004

WebRconEnabled (true/false)
  - Enable/disable Web RCON interface (WebSocket)
  - Can be disabled independently from TCP RCON
  - Default: true

WebRconPort (number)
  - WebSocket HTTP server port
  - Default: 9005

RconPassword (string)
  - Password for authentication (change this!)
  - Default: your_secure_password

UseAceAuthentication (true/false)
  - Use ACE-style packet-based authentication instead of Rust-style URL-based auth
  - Default: false
  - false (Rust-style): Password in WebSocket URL path (e.g., ws://host:9005/password)
                        or first line of TCP connection
  - true (ACE-style): Send {"Command": "auth", "Password": "xxx"} after connecting

MaxConnections (number)
  - Maximum concurrent RCON connections allowed
  - Default: 10

ConnectionTimeoutSeconds (number)
  - Idle connection timeout in seconds
  - Default: 300 (5 minutes)

EnableLogging (true/false)
  - Verbose logging of RCON operations to server console
  - Default: false

DebugMode (true/false)
  - Show full JSON responses and Data objects in web client and server logs
  - Default: false

AutoRefreshPlayers (true/false)
  - Automatically refresh player list on login/logoff events
  - Default: true

MaxReconnectAttempts (number)
  - Maximum reconnection attempts for web client
  - Default: 42 (allows for ~10.5 minutes at 15-second intervals)

ReconnectDelayMs (number)
  - Delay between reconnection attempts in milliseconds
  - Default: 15000 (15 seconds)

ACCESSING THE WEB CLIENT
------------------------

Rust-style Authentication (default, UseAceAuthentication=false):
1. Open your browser to: http://127.0.0.1:9005/ (local) or http://<server-ip>:9005/ (remote)
2. The web client automatically authenticates using the password in the URL path
3. If no saved password, the browser will prompt or you can manually connect to:
   ws://host:9005/your_password

ACE-style Authentication (UseAceAuthentication=true):
1. Open your browser to: http://127.0.0.1:9005/ (local) or http://<server-ip>:9005/ (remote)
2. A login landing page will appear
3. Enter your RCON password and click "Connect"
4. Web client will authenticate via JSON packet

Note: Both TCP RCON (port 9004) and Web RCON (port 9005) accept connections from
ALL network interfaces - accessible locally via 127.0.0.1 and remotely via any IP
address the server is bound to. No admin privileges required.

SERVER CONSOLE COMMANDS
-----------------------

Reload settings without restarting:
  rcon reload

WEB CLIENT FEATURES
-------------------

Console Tab:
  - Real-time server console log streaming
  - Server-side log broadcasting via custom log4net appender
  - Message color coding by type:
    Tag-based colors (takes priority):
      [CHAT] messages - Green (#00ff00)
      [AUDIT] messages - Yellow (#ffff00)
      [SYSTEM] messages - Magenta (#ff00ff)
    Log-level colors (fallback):
      DEBUG - Gray (#888888)
      INFO - Blue (#2196f3)
      WARNING - Orange (#ff9800)
      ERROR - Red (#f44336)
  - Auto-scroll to new messages
  - Customizable colors via color picker (saves to browser local storage)

Players Tab:
  - Online player list with details (name, level, race, location)
  - Player count display
  - Manual refresh button
  - Auto-refresh on player login/logoff (configurable)
  - Auto-refresh preference persists across page reloads

Configuration Tab:
  - Console Colors: Customize message colors with color pickers
  - Server Settings (read-only display):
    - RCON Enabled (TCP server status)
    - Max Connections
    - Connection Timeout
    - Enable Logging
    - Debug Mode
    - Auto Refresh Players
    - Max Reconnect Attempts
    - Reconnect Delay
  - Web RCON Control: Toggle Web RCON on/off independently from TCP RCON
  - Requires server restart for most changes to take effect

Server Info Sidebar:
  - Server status (online/offline)
  - Current player count
  - Server uptime (days, hours, minutes, seconds) - updates on player login/logoff
  - ACE Server version and build number
  - World Database Base and Patch versions
  - Quick command buttons (ACE Commands, List Players, Population, Status, Hello)
  - Stop Server Now button with confirmation dialog (red background, requires explicit confirmation)

AVAILABLE COMMANDS
------------------

All ACE console commands are available via RCON passthrough. The RCON server accepts
ANY console command that works in the ACE server console and returns the command output.

Protocol Commands:
  config      - Get client configuration and auth mode
  hello       - Get initial server state (status, version, player list, database info)
                on authentication
  status      - Get lean server status for periodic polling (no player list)
  players     - Get current player list and count

Common Examples:
  acecommands - Display available ACE commands
  listplayers - List online players
  population  - Show player population info
  help        - Display available commands
  Any other ACE console command (e.g., world broadcast "message", portal create, etc.)

Command Passthrough:
The RCON implementation uses Rust RCON protocol format but with ACE CommandManager
for command execution. Any command that can be executed in the ACE server console
can be executed via RCON.

REAL-TIME FEATURES
------------------
Console Streaming:
  - Custom log4net appender intercepts all server logs
  - Broadcast to all authenticated web clients
  - One-way stream after authentication

Player Events:
  - Login detection via Harmony patch on Player.PlayerEnterWorld()
  - Logoff detection via Harmony patch on PlayerManager.SwitchPlayerFromOnlineToOffline()
  - Events include: player name, GUID, level, location, world time, player count
  - Auto-refresh player list when events occur
  - Player count and uptime updated in real-time

Connection Management:
  - Multiple clients supported (up to MaxConnections)
  - Idle connections timeout after ConnectionTimeoutSeconds
  - Web client auto-reconnects on disconnect
  - Configurable reconnection attempts and delay

SECURITY
--------
- Password-based authentication required
- Pre-authentication users cannot see console logs
- Logs only broadcast to authenticated clients
- Connection timeout prevents idle resource leaks
- Invalid password keeps user on login page (no console access granted)
- Auth mode auto-detected from server (no user confusion with dropdown)
- Stop Server Now requires explicit confirmation dialog

ARCHITECTURE
------------

Real-Time Log Streaming:
  - Server-side: Custom log4net appender (RconLogAppender) intercepts all ACE logs
                 in real-time
  - Broadcasting: RconLogBroadcaster maintains list of connected WebSocket clients
                  and broadcasts logs to all authenticated connections
  - Security: Only authenticated clients receive log messages (one-way after auth)
  - TCP RCON: TCP server can operate independently of web client
  - Web RCON: WebSocket server can be disabled via WebRconEnabled setting

Player Event Detection:
  - Login Detection: Harmony patch on Player.PlayerEnterWorld()
  - Logoff Detection: Harmony patch on PlayerManager.SwitchPlayerFromOnlineToOffline()
  - Broadcasting: Player events broadcast to all connected clients with:
                  - Updated player count
                  - Player name, GUID, level, location
                  - Current world time for uptime calculation
  - Auto-refresh: Web client can automatically refresh player list on these events
  - Real-time uptime: Sidebar uptime updates whenever a player logs in/off

Connection Management:
  - Authentication: Single password-based auth per connection (matches RCON/WebRcon
                    password)
  - Multiple Clients: Supports up to MaxConnections concurrent connections
  - Timeout: Idle connections closed after ConnectionTimeoutSeconds
  - Reconnection: Web client automatically reconnects with configurable attempts
                  and delay
  - Status Tracking: Clients shown connection status and player counts in real-time

DEVELOPMENT & EXTENSION
-----------------------

Adding New Commands:
Edit RconProtocol.cs HandleCommandAsync() method to add new commands:

  case "newcommand" => HandleNewCommand(request),

Log Broadcasting:
Logs are automatically broadcast via RconLogBroadcaster when:
1. Server logs message via ModManager.Log() or log4net
2. RconLogAppender intercepts it
3. Broadcast to all authenticated WebSocket clients

Player Events:
Player events (login/logoff) are automatically broadcast when:
1. Harmony patches fire on player enter/logoff
2. Event data collected and sent to RconLogBroadcaster
3. Broadcast to all authenticated WebSocket clients with updated counts

SERVER CONSOLE COMMANDS
-----------------------
rcon reload    - Reload Settings.json without restarting

TROUBLESHOOTING
---------------
Web client won't load (local)?
  - Check http://127.0.0.1:9005/ in browser
  - Verify WebRconEnabled = true in Settings.json
  - Check firewall allows port 9005

Web client won't load (remote)?
  - Check http://<server-ip>:9005/ in browser
  - Verify firewall allows inbound TCP on port 9005
  - Test connectivity: telnet <server-ip> 9005
  - Both servers bind to all interfaces (0.0.0.0) - no configuration needed
  - No admin privileges required

No console messages appearing?
  - Verify authentication succeeded
  - Check EnableLogging = true for debug output
  - Check server console for errors

Player count not updating?
  - Verify AutoRefreshPlayers = true
  - Check player login/logoff detected in console
  - Manual refresh via Players tab

Colors not matching server?
  - Check message tags in format [TAG]
  - Use Configuration tab color pickers
  - Colors saved to browser localStorage

Connection keeps dropping?
  - Increase MaxReconnectAttempts
  - Increase ReconnectDelayMs for slow networks
  - Check ConnectionTimeoutSeconds isn't too low

TCP RCON connection issues?
  - Test with: telnet 127.0.0.1 9004 (local) or telnet <server-ip> 9004 (remote)
  - Verify RconEnabled = true in Settings.json
  - Check server firewall allows port 9004
  - Works without admin privileges

BUILDING
--------
dotnet build                    - Development build
dotnet build -c Release         - Release build

VERSION HISTORY
---------------
v1.0 - Initial implementation
  - TCP RCON server
  - WebSocket HTTP server
  - Web-based client
  - Password authentication
  - Status, players, landblocks commands

v1.0.x - Phase 1 Refinements
  - Real-time console log streaming via custom log4net appender
  - Player event detection (login/logoff)
  - Auto-refresh player list on events
  - Console message color customization
  - Configuration tab with settings management
  - Separate WebRconEnabled control
  - Configurable reconnection settings
  - Auto-refresh persistence via localStorage
  - Fixed console scrolling layout
  - Tag-based message color parsing ([CHAT], [AUDIT], [SYSTEM], etc.)

v1.0.82 - Protocol & Display Enhancements
  - Added dedicated HELLO command (initial server state with player list)
  - Added dedicated PLAYERS command (player list refresh)
  - Added STATUS command (lean polling response without player list)
  - Display ACE Server version and build number in sidebar
  - Display World Database Base and Patch versions in sidebar
  - Real-time uptime updates on player login/logoff
  - WorldTime included in player events for accurate uptime calculation
  - Raw response logging when DebugMode enabled
  - Optimized RCON protocol commands for efficiency

DOCUMENTATION
--------------
README.md               - Detailed feature documentation (root)
docs/PROTOCOL.md        - JSON protocol and API specification

SUPPORT
-------
For issues or feature requests, refer to the GitHub repository or
check the server logs with EnableLogging = true for detailed debug output.

================================================================================
