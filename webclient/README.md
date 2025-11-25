# RCON Web Client

Simple web-based testing interface for the ACE RCON mod.

## Status
**Phase 0** - Stub/Skeleton only
- Folder structure created
- Documentation in place
- Ready for implementation after Phase 3

## Purpose
- Test RCON mod without building desktop client
- Quick browser-based console for command testing
- Alternative interface to desktop client
- Development/testing tool

## Usage (Future - Phase 3+)
When HTTP server is implemented:
```
http://localhost:2948/
```

## Architecture

### File Structure
```
webclient/
├── README.md (this file)
├── public/
│   ├── index.html (main UI)
│   ├── style.css (styling)
│   └── manifest.json (PWA metadata - future)
└── src/
    ├── rcon-client.js (RCON protocol client)
    ├── ui.js (UI interactions - future)
    └── utils.js (utilities - future)
```

### How It Works (When Implemented)
1. RCON mod starts HTTP server on port 2948
2. Browser accesses `http://localhost:2948/`
3. Mod serves embedded `index.html` from resources
4. JavaScript client connects via WebSocket to RCON
5. Commands sent through WebSocket
6. Responses displayed in browser

### Ports
- **Port 2947** - RCON TCP (desktop clients)
- **Port 2948** - HTTP + WebSocket (web client)

## Development Timeline

| Phase | Status | What |
|-------|--------|------|
| **Phase 0** (now) | ✅ Done | Folder structure, stubs, documentation |
| **Phase 1-2** | - | Build core RCON functionality |
| **Phase 3** | - | Implement RconHttpServer.cs, WebSocket handler |
| **Phase 4** | - | Build out web client features |
| **Phase 5+** | - | Polish, mobile responsive, advanced features |

## Current Status
- Folder structure: ✅ Ready
- Stub files: ✅ Ready
- HTML/CSS/JS templates: ✅ Ready
- HTTP server: ⏳ Phase 3
- WebSocket handler: ⏳ Phase 3
- Full features: ⏳ Phase 4+

## Files to Implement Later

### public/index.html
Main interface for web client
- Server status display
- Command console
- Output log viewer
- Player list (future)
- Real-time dashboard (future)

### src/rcon-client.js
Core RCON protocol implementation for browser
- WebSocket connection
- Message serialization/deserialization
- Authentication
- Command handling
- Event management

### src/ui.js
User interface interactions
- Command input/output
- Status updates
- Live refresh
- Error handling

### src/utils.js
Helper utilities
- Date formatting
- Data transformation
- UI utilities

## Security Notes

When implemented:
- [ ] Require authentication (API key or password)
- [ ] Validate all input on server side
- [ ] Don't expose sensitive data in HTML/JS
- [ ] Consider HTTPS/WSS for production
- [ ] Rate limiting on commands
- [ ] Audit logging for web access

## See Also
- WEBCLIENT_PROPOSAL.md - Detailed technical proposal
- PHASE1_PLAN.md - Core RCON implementation plan
- RCON_Research.md - Protocol specifications

---

*Web client is lowest priority feature. Focus on core RCON (Phase 1-3) first.*
