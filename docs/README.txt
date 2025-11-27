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
- TCP RCON: Port 9004 (RconEnabled setting)
- Web RCON: Port 9005 (WebRconEnabled setting)
  Both can be controlled independently!

NETWORK ACCESS
--------------
✓ Both TCP RCON (9004) and Web RCON (9005) are accessible from ALL network interfaces
✓ Works locally via 127.0.0.1 AND remotely via any server IP address
✓ No admin privileges required on the server
✓ Firewall must allow inbound TCP on ports 9004 and 9005 for remote access

SETTINGS.JSON
-------------
RconEnabled: true/false           - Enable TCP RCON server
RconPort: 9004                    - TCP port (default 9004)
RconPassword: "change_me..."      - Password for authentication
WebRconEnabled: true/false        - Enable Web RCON (WebSocket)
MaxConnections: 10                - Max concurrent connections
ConnectionTimeoutSeconds: 300     - Idle timeout (5 minutes)
EnableLogging: true/false         - Verbose logging
DebugMode: true/false             - Show full JSON responses
AutoRefreshPlayers: true/false    - Auto-refresh on login/logoff
MaxReconnectAttempts: 42          - Reconnection attempts for web client
ReconnectDelayMs: 15000           - Milliseconds between attempts

WEB CLIENT FEATURES
-------------------

Console Tab:
  - Real-time server console streaming
  - Color-coded messages by tag: [CHAT], [AUDIT], [SYSTEM], [INFO], [WARN], [ERROR]
  - Customizable colors via color pickers
  - Auto-scroll to new messages
  - Command input and execution

Players Tab:
  - Online player list (name, level, race, location)
  - Player count display
  - Manual refresh or auto-refresh on login/logoff
  - Auto-refresh preference saved in browser

Configuration Tab:
  - Customize console message colors
  - View current server settings
  - Toggle Web RCON on/off
  - Settings persist across restarts

Server Info Sidebar:
  - Server status (online/offline)
  - Current player count
  - Server uptime (days, hours, minutes, seconds)
  - Quick command buttons

COMMANDS
--------
status      - Server status and statistics
players     - List of online players
landblocks  - Loaded landblock information
help        - Available commands

REAL-TIME FEATURES
------------------
Console Streaming:
  - Custom log4net appender intercepts all server logs
  - Broadcast to all authenticated web clients
  - One-way stream after authentication

Player Events:
  - Login detection via Harmony patch on Player.PlayerEnterWorld()
  - Logoff detection via Harmony patch on PlayerManager.SwitchPlayerFromOnlineToOffline()
  - Auto-refresh player list when events occur
  - Player count updated in real-time

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

DOCUMENTATION
--------------
README.md               - Detailed feature documentation (root)
docs/PROTOCOL.md        - JSON protocol and API specification

SUPPORT
-------
For issues or feature requests, refer to the GitHub repository or
check the server logs with EnableLogging = true for detailed debug output.

================================================================================
