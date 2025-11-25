# ACE.Mod.RCON

RCON (Remote Console) mod for Asheron's Call Emulator (ACE) server. This mod enables remote server administration capabilities similar to modern games like Rust.

## Overview

This mod adds RCON functionality to the ACE server, allowing administrators to connect remotely via a desktop client application (developed separately) to:
- Authenticate with server admin credentials
- Execute console commands remotely
- Monitor real-time server logs
- Manage players (admin functions)
- Access server status and information
- Leverage ACE's versatile data for enhanced functionality beyond standard game RCONs

## Development Status

**Current Phase**: Initial implementation - Login/Connection and Server Status
- [ ] RCON server connection handler
- [ ] Authentication system (server admin account integration)
- [ ] Server status endpoint
- [ ] Real-time log streaming
- [ ] Console command execution
- [ ] Player management functions
- [ ] Security features (IP whitelisting, encryption, rate limiting)

## Architecture

The mod acts as a TCP/UDP server that listens for RCON client connections and processes requests in the same format as standard game RCONs, while supporting ACE-specific commands and data structures.

## Building

```bash
dotnet build
dotnet build -c Release  # Creates release zip
```
