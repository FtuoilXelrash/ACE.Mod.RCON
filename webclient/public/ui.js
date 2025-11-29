/**
 * ACE RCON Web Client UI Handler
 * Manages all UI interactions and updates
 */

let client = null;
let commandHistory = [];
let historyIndex = 0;
let autoRefreshPlayers = true;
let clientConfig = null;
let useAceAuthentication = false; // Will be set from server config (auto-detected)
let historyManagerReady = false; // Track when history manager is ready
// historyManager is created globally by history-manager.js - don't declare it here!

// Console filters - track which message types are filtered
let consoleFilters = {
    aceprogram: false,                // ACE Program messages - default unchecked (show all)
    database: false,                  // Database messages - default unchecked (show all)
    datmanager: false,                // DatManager messages - default unchecked (show all)
    entity: false,                    // Entity messages - default unchecked (show all)
    eventmanager: false,              // EventManager messages - default unchecked (show all)
    guidmanager: false,               // GuidManager messages - default unchecked (show all)
    landblockmanager: false,          // LandblockManager messages - default unchecked (show all)
    managers: false,                  // Managers messages - default unchecked (show all)
    modmanager: false,                // ModManager messages - default unchecked (show all)
    network: false,                   // Network messages - default unchecked (show all)
    playermanager: false,             // PlayerManager messages - default unchecked (show all)
    propertymanager: false,           // PropertyManager messages - default unchecked (show all)
    timestamp: true,                  // Timestamp - default CHECKED (enabled)
    acemodule: false                  // ACE Module - default UNCHECKED (strip module names)
};

/**
 * Initialize the UI and WebSocket client
 */
document.addEventListener('DOMContentLoaded', async function() {
    console.log('[UI] Initializing RCON Web Client');

    // Create RCON client (use current window location)
    client = new RconClient();

    // Set up event listeners
    client.on('server-config', onServerConfig);
    client.on('connected', onConnected);
    client.on('authenticated', onAuthenticated);
    client.on('response', onResponse);
    client.on('error', onError);
    client.on('disconnected', onDisconnected);

    // Set up UI event listeners
    const commandInput = document.getElementById('command-input');
    const sendBtn = document.getElementById('send-btn');
    const worldMessageInput = document.getElementById('world-message-input');
    const sendMsgBtn = document.getElementById('send-msg-btn');

    if (sendBtn) {
        sendBtn.addEventListener('click', sendCommand);
    }

    if (sendMsgBtn) {
        sendMsgBtn.addEventListener('click', sendWorldMessage);
    }

    if (commandInput) {
        commandInput.addEventListener('keypress', function(event) {
            if (event.key === 'Enter' && !event.shiftKey) {
                event.preventDefault();
                sendCommand();
            } else if (event.key === 'ArrowUp') {
                event.preventDefault();
                navigateHistory(-1);
            } else if (event.key === 'ArrowDown') {
                event.preventDefault();
                navigateHistory(1);
            }
        });
    }

    if (worldMessageInput) {
        worldMessageInput.addEventListener('keypress', function(event) {
            if (event.key === 'Enter' && !event.shiftKey) {
                event.preventDefault();
                sendWorldMessage();
            }
        });
    }

    // Initialize history manager for command and message history (fire and forget - don't block UI)
    initializeHistory().catch(err => console.error('[UI] History init error (continuing):', err));

    // Load console filters from localStorage
    loadConsoleFilters();

    // Restore auto-refresh checkbox state from localStorage
    const autoRefreshCheckbox = document.getElementById('auto-refresh-checkbox');
    if (autoRefreshCheckbox) {
        const savedAutoRefresh = localStorage.getItem('autoRefreshPlayers');
        if (savedAutoRefresh !== null) {
            autoRefreshPlayers = savedAutoRefresh === 'true';
            autoRefreshCheckbox.checked = autoRefreshPlayers;
            console.log('[UI] Auto-refresh restored from localStorage:', autoRefreshPlayers);
        }
    }

    // Initialize color style element and restore saved colors
    createStyleElement();

    // Show login page on load
    showLoginPage();

    // Fetch server config on page load to detect auth mode and set correct login title
    fetchServerConfig();
});

/**
 * Fetch server configuration to detect auth mode
 */
async function fetchServerConfig() {
    try {
        console.log('[UI] Fetching server config on page load...');
        await client.connect();
        await client.getConfig();
        // Config will trigger onServerConfig event which updates the UI
        // Prevent auto-reconnect before disconnecting the temporary config connection
        client.disableReconnect = true;
        // Disconnect after getting config so login can establish fresh connection
        client.disconnect();
        // Re-enable reconnect for the actual login connection
        client.disableReconnect = false;
    } catch (error) {
        console.log('[UI] Failed to fetch config on page load (will retry on login):', error);
        // Silently fail - will try again when user clicks login
        if (client.isConnected) {
            client.disableReconnect = true;
            client.disconnect();
            client.disableReconnect = false;
        }
    }
}

/**
 * Update login page UI based on server's UseAceAuthentication setting
 */
function updateAuthModeUI() {
    const loginTitle = document.getElementById('login-title');
    const infoText = document.getElementById('login-info-text');
    const usernameGroup = document.getElementById('login-username-group');

    if (!useAceAuthentication) {
        // Rust-style: password only
        loginTitle.textContent = 'Enter Your RCON Password';
        infoText.textContent = 'RCON Password Authentication is required before access can be granted';
        usernameGroup.style.display = 'none';
    } else {
        // ACE-style: username and password
        loginTitle.textContent = 'Enter Your ACE Admin Credentials';
        infoText.textContent = 'ACE Login/Password Authentication is required before access can be granted';
        usernameGroup.style.display = 'flex';
    }
}

/**
 * Handle login form submission
 */
async function handleLogin(event) {
    event.preventDefault();
    const password = document.getElementById('login-password').value;
    const username = document.getElementById('login-username')?.value || '';
    const loginBtn = document.getElementById('login-btn');
    const loginError = document.getElementById('login-error');

    loginBtn.disabled = true;
    loginError.style.display = 'none';

    if (!password) {
        loginError.textContent = 'Password is required';
        loginError.style.display = 'block';
        loginBtn.disabled = false;
        return;
    }

    try {
        console.log('[UI] Attempting login...');

        // Disconnect any existing connection first
        if (client.isConnected) {
            client.disconnect();
        }

        // Try ACE-style auth first if username is provided
        if (username && username.trim()) {
            console.log('[UI] Attempting ACE-style authentication with account:', username);

            // Clear password from URL path (ACE auth doesn't use it)
            client.password = null;

            // Connect to /rcon endpoint (no password in URL)
            await client.connect();

            // Send ACE auth command
            // Auth response includes server status data, so we don't need separate HELLO
            await client.authenticate(password, username);
            // Success - onAuthenticated() will handle showing console and displaying auth response data
        } else {
            console.log('[UI] Attempting Rust-style RCON authentication');

            // Rust-style: password only, include in URL
            client.setPassword(password);
            localStorage.setItem('rconPassword', password);

            // Connect with password in URL
            await client.connect();

            // Validate we're authenticated by sending HELLO
            await client.send('hello', []);
            // Success - onConnected() will handle showing console
        }
    } catch (err) {
        console.error('[UI] Login failed:', err);
        loginError.textContent = err.message || 'Login failed. Check password/account name.';
        loginError.style.display = 'block';
        loginBtn.disabled = false;
    }
}

/**
 * Show the login page
 */
function showLoginPage() {
    console.log('[UI] Showing login page');
    const loginPage = document.getElementById('login-page');
    const mainContainer = document.getElementById('main-container');

    if (loginPage) loginPage.style.display = 'flex';
    if (mainContainer) mainContainer.style.display = 'none';

    // Focus password input
    setTimeout(() => {
        const passwordInput = document.getElementById('login-password');
        if (passwordInput) passwordInput.focus();
    }, 100);
}

/**
 * Hide the login page and show main UI
 */
function hideLoginPage() {
    console.log('[UI] Hiding login page');
    const loginPage = document.getElementById('login-page');
    const mainContainer = document.getElementById('main-container');

    if (loginPage) loginPage.style.display = 'none';
    if (mainContainer) mainContainer.style.display = 'flex';
}

/**
 * Handle server configuration response
 * (Called after connection, updates login page with correct auth mode)
 */
function onServerConfig(config) {
    console.log('[UI] Server config received:', config);
    useAceAuthentication = config.UseAceAuthentication || false;
    console.log('[UI] UseAceAuthentication:', useAceAuthentication);

    // Update login page UI to match server's auth mode
    updateAuthModeUI();
}

/**
 * Connect to RCON server
 */
async function connectToServer() {
    try {
        console.log('[UI] Connecting to server with auth mode - UseAceAuthentication:', useAceAuthentication);
        await client.connect();
        console.log('[UI] Connected to server');
        return Promise.resolve();
    } catch (error) {
        console.error('[UI] Connection failed:', error);

        // Show error on login page
        const loginError = document.getElementById('login-error');
        const loginBtn = document.getElementById('login-btn');

        if (!useAceAuthentication) {
            // For Rust-style, connection failure likely means invalid password in URL
            if (loginError) {
                loginError.textContent = 'Connection failed - Invalid password in URL';
                loginError.style.display = 'block';
            }
        } else {
            // For ACE-style, connection failure is network error
            if (loginError) {
                loginError.textContent = 'Failed to connect to server: ' + error.message;
                loginError.style.display = 'block';
            }
        }

        if (loginBtn) loginBtn.disabled = false;

        throw error;
    }
}

/**
 * Called when WebSocket connects
 */
function onConnected() {
    console.log('[UI] WebSocket connected');
    updateStatus('connected', 'Connected - Authenticating...');

    // Don't send HELLO here - let handleLogin control the auth flow
    // For Rust-style: handleLogin will send HELLO
    // For ACE-style: handleLogin will send auth command
}

/**
 * Called when authenticated (either via Rust-style connection or ACE auth command)
 */
function onAuthenticated(authResponse) {
    console.log('[UI] Authenticated event fired, client.isAuthenticated:', client.isAuthenticated);
    // Safety check: ensure client's isAuthenticated flag is set before showing console
    if (!client.isAuthenticated) {
        console.error('[UI] SECURITY: onAuthenticated() fired but client.isAuthenticated is false!');
        return;
    }
    handleAuthenticationComplete(authResponse);
}

/**
 * Handle successful authentication (common for both auth modes)
 */
function handleAuthenticationComplete(authResponse) {
    // Safety check: only show console if we're actually authenticated
    if (!client.isAuthenticated) {
        console.error('[UI] SECURITY: handleAuthenticationComplete() called but not authenticated!');
        console.log('[UI] Closing connection and returning to login');
        client.ws.close();
        showLoginPage();
        return;
    }

    updateStatus('authenticated', 'Authenticated');

    // Hide login page
    hideLoginPage();

    // Re-enable login button for future use
    const loginBtn = document.getElementById('login-btn');
    if (loginBtn) loginBtn.disabled = false;

    addOutput('Successfully authenticated!', 'success-message');

    // Update sidebar with status data from auth response if available
    if (authResponse && authResponse.Data) {
        updateSidebarPanel(authResponse);
    }

    // Fetch fresh server state via HELLO command
    client.send('hello', []).then(response => {
        console.log('[UI] HELLO response received');
        // Update sidebar with fresh data from HELLO
        if (response && response.Data) {
            updateSidebarPanel(response);
        }
        // Display players if in HELLO response
        if (response.Data && response.Data.OnlinePlayers) {
            displayPlayers({ players: response.Data.OnlinePlayers });
        }
    }).catch(err => {
        console.warn('[UI] HELLO command failed (non-critical):', err);
        // Don't error out - we already have auth data
    });

    // Fetch client configuration from server
    setTimeout(() => {
        fetchClientConfig();
    }, 500);

    // Enable UI
    enableCommands();

    // Refresh history dropdowns after auth (in case they weren't loaded yet)
    if (historyManagerReady && historyManager) {
        console.log('[handleAuthenticationComplete] Refreshing history dropdowns');
        updateCommandHistoryDropdown();
        updateMessageHistoryDropdown();
    }

    // Update input placeholder
    const commandInput = document.getElementById('command-input');
    if (commandInput) {
        commandInput.placeholder = 'Enter RCON command...';
        commandInput.value = '';
        commandInput.focus();
    }
}

/**
 * Called when receiving a response
 */
function onResponse(response) {
    console.log('[UI] Received response:', response);

    // Handle broadcast log messages
    if (response.Status && response.Status.startsWith('log_')) {
        handleLogMessage(response);
        return;
    }

    // Handle player events (login/logoff)
    if (response.Status === 'player_event') {
        handlePlayerEvent(response);
        return;
    }

    // Handle server status updates
    if (response.Status === 'status_update') {
        if (response.Data) {
            updateSidebarPanel(response);
        }
        return;
    }

    // Check for auth response
    if (response.Status === 'authenticated' || response.Status === 'success') {
        if (response.Command === 'auth') {
            // Auth was successful - let authenticated event handle it
            return;
        }
    }

    // Check for auth error - keep user on login page
    if (response.Status === 'error' && response.Command === 'auth') {
        // Auth failed - show error on login page
        const loginError = document.getElementById('login-error');
        const loginBtn = document.getElementById('login-btn');
        if (loginError) {
            loginError.textContent = response.Message || 'Authentication failed - Invalid password';
            loginError.style.display = 'block';
        }
        if (loginBtn) loginBtn.disabled = false;
        // Don't display to console, and don't proceed to main UI
        return;
    }

    // Update sidebar with data if available
    if (response.Data) {
        updateSidebarPanel(response);

        // Display players in Players tab if this is a players/hello response
        if (response.Data.OnlinePlayers && Array.isArray(response.Data.OnlinePlayers)) {
            displayPlayers({ players: response.Data.OnlinePlayers });
        }
    }

    // Display response (but not if not authenticated)
    if (!client.isAuthenticated && response.Status === 'error') {
        // Don't show unauthenticated errors in console
        return;
    }

    if (response.Status === 'error') {
        addOutput(`Error: ${response.Message}`, 'error-message');
    } else {
        // Always show message text first
        if (response.Message) {
            let displayText = response.Message.replace(/\\r\\n/g, '\n').replace(/\\n/g, '\n');
            addOutput(displayText, 'response-message');
        }

        // Show full JSON response only in debug mode
        // Check if response indicates debug mode is enabled
        if (response.Debug === true) {
            try {
                const formatted = JSON.stringify(response, null, 2);
                addOutput('---\nRaw Response:\n' + formatted, 'debug-message');
            } catch (e) {
                // Fallback if JSON serialization fails
                addOutput('---\nRaw Response: ' + JSON.stringify(response), 'debug-message');
            }
        }
    }
}

/**
 * Update sidebar panel with status data
 */
function updateSidebarPanel(response) {
    try {
        const data = response.Data;
        if (!data) return;

        // Update server name
        const nameEl = document.getElementById('server-name');
        if (nameEl && data.ServerName) {
            nameEl.textContent = data.ServerName;
        }

        // Update from status command
        if (data.Status || data.Uptime) {
            const statusEl = document.getElementById('server-status');
            const playerEl = document.getElementById('player-count');
            const uptimeEl = document.getElementById('uptime');

            if (statusEl && data.Status) {
                statusEl.textContent = data.Status;
            }
            if (playerEl && data.CurrentPlayers !== undefined) {
                playerEl.textContent = data.CurrentPlayers;
            }
            if (uptimeEl && data.Uptime) {
                uptimeEl.textContent = data.Uptime;
            }
        }

        // Update version info
        if (data.AceServerVersion) {
            const aceServerVersionEl = document.getElementById('ace-server-version');
            if (aceServerVersionEl) {
                aceServerVersionEl.textContent = data.AceServerVersion;
            }
        }
        if (data.AceServerBuild) {
            const aceServerBuildEl = document.getElementById('ace-server-build');
            if (aceServerBuildEl) {
                aceServerBuildEl.textContent = data.AceServerBuild;
            }
        }
        if (data.AceDatabaseBaseVersion) {
            const aceDatabaseBaseEl = document.getElementById('ace-database-base');
            if (aceDatabaseBaseEl) {
                aceDatabaseBaseEl.textContent = data.AceDatabaseBaseVersion;
            }
        }
        if (data.AceDatabasePatchVersion) {
            const aceDatabasePatchEl = document.getElementById('ace-database-patch');
            if (aceDatabasePatchEl) {
                aceDatabasePatchEl.textContent = data.AceDatabasePatchVersion;
            }
        }

        // Update from players command
        if (data.players && data.count !== undefined) {
            const playerEl = document.getElementById('player-count');
            if (playerEl) {
                playerEl.textContent = data.count;
            }
        }
    } catch (e) {
        console.error('[UI] Error updating sidebar:', e);
    }
}

/**
 * Called on error
 */
function onError(error) {
    console.error('[UI] Error:', error);
    addOutput(`Error: ${error.message || 'Unknown error'}`, 'error-message');
}

/**
 * Called when disconnected
 */
function onDisconnected() {
    console.log('[UI] Disconnected');
    updateStatus('disconnected', 'Disconnected');
    addOutput('Disconnected from server.', 'info-message');
    disableCommands();

    // Clear all server info
    const serverNameEl = document.getElementById('server-name');
    if (serverNameEl) {
        serverNameEl.textContent = '';
    }

    const serverStatusEl = document.getElementById('server-status');
    if (serverStatusEl) {
        serverStatusEl.textContent = 'Not Connected';
    }

    const playerCountEl = document.getElementById('player-count');
    if (playerCountEl) {
        playerCountEl.textContent = '';
    }

    const uptimeEl = document.getElementById('uptime');
    if (uptimeEl) {
        uptimeEl.textContent = '';
    }

    const aceServerVersionEl = document.getElementById('ace-server-version');
    if (aceServerVersionEl) {
        aceServerVersionEl.textContent = '-';
    }

    const aceServerBuildEl = document.getElementById('ace-server-build');
    if (aceServerBuildEl) {
        aceServerBuildEl.textContent = '-';
    }

    const aceDatabaseBaseEl = document.getElementById('ace-database-base');
    if (aceDatabaseBaseEl) {
        aceDatabaseBaseEl.textContent = '-';
    }

    const aceDatabasePatchEl = document.getElementById('ace-database-patch');
    if (aceDatabasePatchEl) {
        aceDatabasePatchEl.textContent = '-';
    }

    // Clear players list
    const playersList = document.getElementById('players-list');
    if (playersList) {
        playersList.innerHTML = '<div class="info-message">Players will appear here after authentication</div>';
    }
}

/**
 * Send a command
 */
async function sendCommand() {
    const commandInput = document.getElementById('command-input');
    if (!commandInput) return;

    let command = commandInput.value.trim();
    if (!command) return;

    // Check if we're authenticating
    if (!client.isAuthenticated && !client.isConnected) {
        // Try to connect
        await connectToServer();
        return;
    }

    if (!client.isAuthenticated && client.isConnected) {
        // Try to authenticate with the password
        try {
            commandInput.disabled = true;
            addOutput(`> auth ${command}`, 'command-message');

            await client.authenticate(command);

            // Clear input on success
            commandInput.value = '';
        } catch (error) {
            addOutput(`Authentication failed: ${error.message}`, 'error-message');
            commandInput.value = '';
        } finally {
            commandInput.disabled = false;
            commandInput.focus();
        }
        return;
    }

    // Send regular command
    if (!client.isAuthenticated) {
        addOutput('Not authenticated', 'error-message');
        return;
    }

    // Parse command and args
    const parts = command.split(/\s+/);
    const cmd = parts[0];
    const args = parts.slice(1);

    try {
        commandInput.disabled = true;
        addOutput(`> ${command}`, 'command-message');

        const response = await client.send(cmd, args);

        // Response is handled by onResponse
    } catch (error) {
        addOutput(`Command error: ${error.message}`, 'error-message');
    } finally {
        commandInput.disabled = false;
        commandInput.value = '';
        commandInput.focus();

        // Add to history manager and update dropdown
        console.log('[sendCommand] Attempting to add to history:', command);
        console.log('[sendCommand] historyManagerReady:', historyManagerReady);

        // Try to get historyManager from window if local variable isn't set
        let mgr = historyManager;
        if (!mgr && typeof window.historyManager !== 'undefined') {
            console.log('[sendCommand] Using window.historyManager (local not set)');
            mgr = window.historyManager;
        }
        console.log('[sendCommand] mgr exists:', typeof mgr !== 'undefined');

        if (mgr && typeof mgr.addCommand === 'function') {
            try {
                mgr.addCommand(command);
                console.log('[sendCommand] ✓ Command added to history');
                console.log('[sendCommand] History now contains:', mgr.getCommands());
                updateCommandHistoryDropdown();
                console.log('[sendCommand] ✓ Dropdown updated');
            } catch (e) {
                console.error('[sendCommand] Error adding to history:', e);
            }
        } else {
            console.warn('[sendCommand] History manager NOT available');
            console.warn('  - historyManagerReady:', historyManagerReady);
            console.warn('  - local historyManager:', typeof historyManager !== 'undefined');
            console.warn('  - window.historyManager:', typeof window.historyManager !== 'undefined');
        }

        // Also maintain legacy command history for arrow key navigation
        commandHistory.push(command);
        historyIndex = commandHistory.length;
    }
}

/**
 * Send acecommands command
 */
async function aceCommands() {
    if (!client.isAuthenticated) {
        addOutput('Not authenticated', 'error-message');
        return;
    }

    try {
        const aceCommandsBtn = document.getElementById('ace-commands-btn');
        if (aceCommandsBtn) aceCommandsBtn.disabled = true;

        addOutput('> acecommands', 'command-message');

        const response = await client.send('acecommands', []);

        // Response is handled by onResponse
    } catch (error) {
        addOutput(`Command error: ${error.message}`, 'error-message');
    } finally {
        const aceCommandsBtn = document.getElementById('ace-commands-btn');
        if (aceCommandsBtn) aceCommandsBtn.disabled = false;
    }
}

/**
 * Send listplayers command
 */
async function listPlayers() {
    if (!client.isAuthenticated) {
        addOutput('Not authenticated', 'error-message');
        return;
    }

    try {
        const listPlayersBtn = document.getElementById('list-players-btn');
        if (listPlayersBtn) listPlayersBtn.disabled = true;

        addOutput('> listplayers', 'command-message');

        const response = await client.send('listplayers', []);

        // Response is handled by onResponse
    } catch (error) {
        addOutput(`Command error: ${error.message}`, 'error-message');
    } finally {
        const listPlayersBtn = document.getElementById('list-players-btn');
        if (listPlayersBtn) listPlayersBtn.disabled = false;
    }
}

/**
 * Send population command
 */
async function population() {
    if (!client.isAuthenticated) {
        addOutput('Not authenticated', 'error-message');
        return;
    }

    try {
        const populationBtn = document.getElementById('population-btn');
        if (populationBtn) populationBtn.disabled = true;

        addOutput('> pop', 'command-message');

        const response = await client.send('pop', []);

        // Response is handled by onResponse
    } catch (error) {
        addOutput(`Command error: ${error.message}`, 'error-message');
    } finally {
        const populationBtn = document.getElementById('population-btn');
        if (populationBtn) populationBtn.disabled = false;
    }
}

/**
 * Send status command - returns detailed server status
 */
async function statusCommand() {
    if (!client.isAuthenticated) {
        addOutput('Not authenticated', 'error-message');
        return;
    }

    try {
        const statusBtn = document.getElementById('status-btn');
        if (statusBtn) statusBtn.disabled = true;

        addOutput('> status', 'command-message');

        const response = await client.send('status', []);

        // Response is handled by onResponse
    } catch (error) {
        addOutput(`Command error: ${error.message}`, 'error-message');
    } finally {
        const statusBtn = document.getElementById('status-btn');
        if (statusBtn) statusBtn.disabled = false;
    }
}

/**
 * Send HELLO command to get initial server state
 */
async function helloCommand() {
    if (!client.isAuthenticated) {
        addOutput('Not authenticated', 'error-message');
        return;
    }

    try {
        const helloBtn = document.getElementById('hello-btn');
        if (helloBtn) helloBtn.disabled = true;

        addOutput('> hello', 'command-message');

        const response = await client.send('hello', []);

        // Response is handled by onResponse
    } catch (error) {
        addOutput(`Command error: ${error.message}`, 'error-message');
    } finally {
        const helloBtn = document.getElementById('hello-btn');
        if (helloBtn) helloBtn.disabled = false;
    }
}

/**
 * Stop server immediately
 */
async function stopNow() {
    if (!client.isAuthenticated) {
        addOutput('Not authenticated', 'error-message');
        return;
    }

    // Confirm before stopping
    if (!confirm('Are you sure you want to stop the server immediately? This will disconnect all players!')) {
        return;
    }

    try {
        const stopBtn = document.getElementById('stop-now-btn');
        if (stopBtn) stopBtn.disabled = true;

        addOutput('> stop-now', 'command-message');

        const response = await client.send('stop-now', []);

        // Response is handled by onResponse
    } catch (error) {
        addOutput(`Command error: ${error.message}`, 'error-message');
    } finally {
        const stopBtn = document.getElementById('stop-now-btn');
        if (stopBtn) stopBtn.disabled = false;
    }
}

/**
 * Send world open command
 */
async function openWorld() {
    if (!client.isAuthenticated) {
        addOutput('Not authenticated', 'error-message');
        return;
    }

    try {
        const openBtn = document.getElementById('open-world-btn');
        if (openBtn) openBtn.disabled = true;

        addOutput('> world open', 'command-message');

        const response = await client.send('world', ['open']);

        // Response is handled by onResponse
    } catch (error) {
        addOutput(`Command error: ${error.message}`, 'error-message');
    } finally {
        const openBtn = document.getElementById('open-world-btn');
        if (openBtn) openBtn.disabled = false;
    }
}

/**
 * Send world close command
 */
async function closeWorld() {
    if (!client.isAuthenticated) {
        addOutput('Not authenticated', 'error-message');
        return;
    }

    try {
        const closeBtn = document.getElementById('close-world-btn');
        if (closeBtn) closeBtn.disabled = true;

        addOutput('> world close', 'command-message');

        const response = await client.send('world', ['close']);

        // Response is handled by onResponse
    } catch (error) {
        addOutput(`Command error: ${error.message}`, 'error-message');
    } finally {
        const closeBtn = document.getElementById('close-world-btn');
        if (closeBtn) closeBtn.disabled = false;
    }
}

/**
 * Toggle console filter on/off
 */
function toggleConsoleFilter(filterName, isChecked) {
    console.log('[UI] Toggling filter:', filterName, 'isChecked:', isChecked);
    consoleFilters[filterName] = isChecked;

    // Save filter state to localStorage
    localStorage.setItem('consoleFilters', JSON.stringify(consoleFilters));
    console.log('[UI] Filter state saved:', consoleFilters);
}

/**
 * Load console filters from localStorage
 */
function loadConsoleFilters() {
    const saved = localStorage.getItem('consoleFilters');
    if (saved) {
        try {
            const filters = JSON.parse(saved);
            consoleFilters = { ...consoleFilters, ...filters };
            console.log('[UI] Loaded console filters:', consoleFilters);

            // Update checkbox UI
            const aceProgramCheckbox = document.getElementById('filter-ace-program');
            if (aceProgramCheckbox) {
                aceProgramCheckbox.checked = consoleFilters.aceprogram;
            }
            const databaseCheckbox = document.getElementById('filter-database');
            if (databaseCheckbox) {
                databaseCheckbox.checked = consoleFilters.database;
            }
            const datCheckbox = document.getElementById('filter-dat-manager');
            if (datCheckbox) {
                datCheckbox.checked = consoleFilters.datmanager;
            }
            const entityCheckbox = document.getElementById('filter-entity');
            if (entityCheckbox) {
                entityCheckbox.checked = consoleFilters.entity;
            }
            const eventManagerCheckbox = document.getElementById('filter-event-manager');
            if (eventManagerCheckbox) {
                eventManagerCheckbox.checked = consoleFilters.eventmanager;
            }
            const guidManagerCheckbox = document.getElementById('filter-guid-manager');
            if (guidManagerCheckbox) {
                guidManagerCheckbox.checked = consoleFilters.guidmanager;
            }
            const landblockManagerCheckbox = document.getElementById('filter-landblock-manager');
            if (landblockManagerCheckbox) {
                landblockManagerCheckbox.checked = consoleFilters.landblockmanager;
            }
            const managersCheckbox = document.getElementById('filter-managers');
            if (managersCheckbox) {
                managersCheckbox.checked = consoleFilters.managers;
            }
            const playerManagerCheckbox = document.getElementById('filter-player-manager');
            if (playerManagerCheckbox) {
                playerManagerCheckbox.checked = consoleFilters.playermanager;
            }
            const propertyManagerCheckbox = document.getElementById('filter-property-manager');
            if (propertyManagerCheckbox) {
                propertyManagerCheckbox.checked = consoleFilters.propertymanager;
            }
            const modCheckbox = document.getElementById('filter-mod-manager');
            if (modCheckbox) {
                modCheckbox.checked = consoleFilters.modmanager;
            }
            const networkCheckbox = document.getElementById('filter-network');
            if (networkCheckbox) {
                networkCheckbox.checked = consoleFilters.network;
            }
            const timestampCheckbox = document.getElementById('filter-timestamp');
            if (timestampCheckbox) {
                timestampCheckbox.checked = consoleFilters.timestamp;
            }
            const aceModuleCheckbox = document.getElementById('filter-ace-module');
            if (aceModuleCheckbox) {
                aceModuleCheckbox.checked = consoleFilters.acemodule;
            }
        } catch (e) {
            console.error('[UI] Failed to load console filters:', e);
        }
    }
}

/**
 * Navigate command history
 */
function navigateHistory(direction) {
    const commandInput = document.getElementById('command-input');
    if (!commandInput) return;

    if (direction === -1) {
        // Up
        if (historyIndex > 0) {
            historyIndex--;
            commandInput.value = commandHistory[historyIndex] || '';
        }
    } else if (direction === 1) {
        // Down
        if (historyIndex < commandHistory.length - 1) {
            historyIndex++;
            commandInput.value = commandHistory[historyIndex] || '';
        } else {
            historyIndex = commandHistory.length;
            commandInput.value = '';
        }
    }

    // Move cursor to end
    commandInput.setSelectionRange(commandInput.value.length, commandInput.value.length);
}

/**
 * Add message to output
 */
function addOutput(message, className = '') {
    const output = document.getElementById('output');
    if (!output) return;

    // Check if this message should be filtered
    if (consoleFilters.landblockmanager && message.includes('[ACE.Server.Managers.LandblockManager]')) {
        return; // Skip this message - filter is active
    }
    if (consoleFilters.managers && message.includes('[ACE.Server.Managers.')) {
        return; // Skip this message - filter is active
    }
    if (consoleFilters.modmanager && message.includes('[ACE.Server.Mods.')) {
        return; // Skip this message - filter is active
    }
    if (consoleFilters.datmanager && message.includes('[ACE.DatLoader.DatManager]')) {
        return; // Skip this message - filter is active
    }
    if (consoleFilters.entity && message.includes('[ACE.Server.Entity')) {
        return; // Skip this message - filter is active
    }
    if (consoleFilters.eventmanager && message.includes('[ACE.Server.Managers.EventManager]')) {
        return; // Skip this message - filter is active
    }
    if (consoleFilters.guidmanager && message.includes('[ACE.Server.Managers.GuidManager]')) {
        return; // Skip this message - filter is active
    }
    if (consoleFilters.landblockmanager && message.includes('[ACE.Server.Managers.LandblockManager]')) {
        return; // Skip this message - filter is active
    }
    if (consoleFilters.playermanager && message.includes('[ACE.Server.Managers.PlayerManager]')) {
        return; // Skip this message - filter is active
    }
    if (consoleFilters.propertymanager && message.includes('[ACE.Server.Managers.PropertyManager]')) {
        return; // Skip this message - filter is active
    }
    if (consoleFilters.aceprogram && message.includes('[ACE.Server.Program]')) {
        return; // Skip this message - filter is active
    }
    if (consoleFilters.database && message.includes('[ACE.Database.')) {
        return; // Skip this message - filter is active
    }
    if (consoleFilters.network && message.includes('[ACE.Server.Network')) {
        return; // Skip this message - filter is active
    }

    const line = document.createElement('div');
    if (className) {
        line.className = className;
    }

    // Strip ACE module name if disabled
    let displayMessage = message;
    if (!consoleFilters.acemodule) {
        // Remove [ACE.xxx.yyy] pattern from the beginning of the message
        displayMessage = displayMessage.replace(/^\[ACE\.[^\]]+\]\s*/, '');
    }

    // Add timestamp if enabled
    if (consoleFilters.timestamp) {
        const now = new Date();
        const timeString = now.toLocaleTimeString('en-US', {
            hour12: false,
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit'
        });
        displayMessage = `[${timeString}] ${displayMessage}`;
    }

    // Handle multi-line messages (like formatted JSON)
    if (displayMessage.includes('\n')) {
        const pre = document.createElement('pre');
        pre.textContent = displayMessage;
        line.appendChild(pre);
    } else {
        line.textContent = displayMessage;
    }

    output.appendChild(line);

    // Auto-scroll to bottom - scroll the output container, not the page
    // Directly set scrollTop immediately without delay
    output.scrollTop = output.scrollHeight;
}

/**
 * Update connection status display
 */
function updateStatus(status, text) {
    const statusDiv = document.getElementById('status');
    const statusText = document.getElementById('status-text');

    if (statusDiv) {
        statusDiv.className = `status ${status}`;
    }

    if (statusText) {
        statusText.textContent = text;
    }

    console.log(`[UI] Status: ${status} - ${text}`);
}

/**
 * Enable command input
 */
function enableCommands() {
    const commandInput = document.getElementById('command-input');
    const sendBtn = document.getElementById('send-btn');
    const worldMessageInput = document.getElementById('world-message-input');
    const sendMsgBtn = document.getElementById('send-msg-btn');
    const commandHistoryDropdown = document.getElementById('command-history-dropdown');
    const messageHistoryDropdown = document.getElementById('message-history-dropdown');
    const quickButtons = document.querySelectorAll('.quick-commands button');
    const refreshPlayersBtn = document.getElementById('refresh-players-btn');
    const aceCommandsBtn = document.getElementById('ace-commands-btn');
    const listPlayersBtn = document.getElementById('list-players-btn');
    const populationBtn = document.getElementById('population-btn');
    const statusBtn = document.getElementById('status-btn');
    const helloBtn = document.getElementById('hello-btn');
    const stopNowBtn = document.getElementById('stop-now-btn');
    const openWorldBtn = document.getElementById('open-world-btn');
    const closeWorldBtn = document.getElementById('close-world-btn');
    const fetchBansBtn = document.getElementById('fetch-bans-btn');

    if (commandInput) commandInput.disabled = false;
    if (sendBtn) sendBtn.disabled = false;
    if (worldMessageInput) worldMessageInput.disabled = false;
    if (sendMsgBtn) sendMsgBtn.disabled = false;
    if (commandHistoryDropdown) commandHistoryDropdown.disabled = false;
    if (messageHistoryDropdown) messageHistoryDropdown.disabled = false;
    if (refreshPlayersBtn) refreshPlayersBtn.disabled = false;
    if (aceCommandsBtn) aceCommandsBtn.disabled = false;
    if (listPlayersBtn) listPlayersBtn.disabled = false;
    if (populationBtn) populationBtn.disabled = false;
    if (statusBtn) statusBtn.disabled = false;
    if (helloBtn) helloBtn.disabled = false;
    if (stopNowBtn) stopNowBtn.disabled = false;
    if (openWorldBtn) openWorldBtn.disabled = false;
    if (closeWorldBtn) closeWorldBtn.disabled = false;
    if (fetchBansBtn) fetchBansBtn.disabled = false;

    quickButtons.forEach(btn => {
        btn.disabled = false;
    });
}

/**
 * Disable command input
 */
function disableCommands() {
    const commandInput = document.getElementById('command-input');
    const sendBtn = document.getElementById('send-btn');
    const worldMessageInput = document.getElementById('world-message-input');
    const sendMsgBtn = document.getElementById('send-msg-btn');
    const commandHistoryDropdown = document.getElementById('command-history-dropdown');
    const messageHistoryDropdown = document.getElementById('message-history-dropdown');
    const quickButtons = document.querySelectorAll('.quick-commands button');
    const refreshPlayersBtn = document.getElementById('refresh-players-btn');
    const aceCommandsBtn = document.getElementById('ace-commands-btn');
    const listPlayersBtn = document.getElementById('list-players-btn');
    const populationBtn = document.getElementById('population-btn');
    const statusBtn = document.getElementById('status-btn');
    const helloBtn = document.getElementById('hello-btn');
    const stopNowBtn = document.getElementById('stop-now-btn');
    const openWorldBtn = document.getElementById('open-world-btn');
    const closeWorldBtn = document.getElementById('close-world-btn');
    const fetchBansBtn = document.getElementById('fetch-bans-btn');

    if (commandInput) commandInput.disabled = true;
    if (sendBtn) sendBtn.disabled = true;
    if (worldMessageInput) worldMessageInput.disabled = true;
    if (sendMsgBtn) sendMsgBtn.disabled = true;
    if (commandHistoryDropdown) commandHistoryDropdown.disabled = true;
    if (messageHistoryDropdown) messageHistoryDropdown.disabled = true;
    if (refreshPlayersBtn) refreshPlayersBtn.disabled = true;
    if (aceCommandsBtn) aceCommandsBtn.disabled = true;
    if (listPlayersBtn) listPlayersBtn.disabled = true;
    if (populationBtn) populationBtn.disabled = true;
    if (statusBtn) statusBtn.disabled = true;
    if (helloBtn) helloBtn.disabled = true;
    if (stopNowBtn) stopNowBtn.disabled = true;
    if (openWorldBtn) openWorldBtn.disabled = true;
    if (closeWorldBtn) closeWorldBtn.disabled = true;
    if (fetchBansBtn) fetchBansBtn.disabled = true;

    quickButtons.forEach(btn => {
        btn.disabled = true;
    });
}

/**
 * Switch between tabs
 */
function switchTab(tabId) {
    // Hide all tabs
    const tabContents = document.querySelectorAll('.tab-content');
    tabContents.forEach(tab => tab.classList.remove('active'));

    // Remove active class from all buttons
    const tabButtons = document.querySelectorAll('.tab-button');
    tabButtons.forEach(btn => btn.classList.remove('active'));

    // Show selected tab
    const selectedTab = document.getElementById(tabId);
    if (selectedTab) {
        selectedTab.classList.add('active');
    }

    // Mark button as active
    event.target.classList.add('active');

    // Show appropriate sidebar based on active tab
    const mainSidebar = document.getElementById('sidebar');
    const playersSidebar = document.getElementById('players-sidebar');
    const configSidebar = document.getElementById('config-sidebar');

    if (mainSidebar) {
        mainSidebar.style.display = (tabId === 'console-tab') ? 'flex' : 'none';
    }
    if (playersSidebar) {
        playersSidebar.style.display = (tabId === 'players-tab') ? 'flex' : 'none';
        // Clear player selection when switching away from Players tab
        if (tabId !== 'players-tab') {
            document.querySelectorAll('.player-item.selected').forEach(item => {
                item.classList.remove('selected');
            });
        }
    }
    if (configSidebar) {
        configSidebar.style.display = (tabId === 'config-tab') ? 'flex' : 'none';
    }
}

/**
 * Refresh players list
 */
async function refreshPlayers() {
    if (!client.isAuthenticated) {
        addOutput('Not authenticated', 'error-message');
        return;
    }

    const refreshBtn = document.getElementById('refresh-players-btn');
    refreshBtn.disabled = true;
    refreshBtn.textContent = 'Loading...';

    try {
        const response = await client.send('players', []);
        // Response will be handled by onResponse()
    } catch (error) {
        addOutput(`Failed to refresh players: ${error.message}`, 'error-message');
    } finally {
        refreshBtn.disabled = false;
        refreshBtn.textContent = 'Refresh Players';
    }
}

/**
 * Display players in the Players tab
 */
function displayPlayers(playersData) {
    const playersList = document.getElementById('players-list');

    if (!playersData || !playersData.players || playersData.players.length === 0) {
        playersList.innerHTML = '<div class="info-message">No players online</div>';
        return;
    }

    let html = '';
    playersData.players.forEach((player, index) => {
        // Handle both object and string representations
        let playerName = player.Name || player.name || 'Unknown';
        let playerLevel = player.Level || player.level || 'N/A';
        let playerRace = player.Race || player.race || '';
        let accountName = player.AccountName || player.accountName || '';

        let details = `Level: ${playerLevel}`;
        if (playerRace) details += ` | Race: ${playerRace}`;
        if (accountName) details += ` | Account: ${accountName}`;

        html += `
            <div class="player-item" onclick="togglePlayerSelect(this, '${playerName}', '${accountName}')">
                <div class="player-info">
                    <span class="player-name">${playerName}</span>
                    <span class="player-details" style="font-size: 11px; color: #aaa;">${details}</span>
                </div>
            </div>
        `;
    });

    playersList.innerHTML = html;
}

/**
 * Toggle player selection (single-select only)
 */
function togglePlayerSelect(element, playerName, accountName) {
    // Deselect all other players
    document.querySelectorAll('.player-item.selected').forEach(item => {
        item.classList.remove('selected');
    });

    // Select current player
    element.classList.add('selected');
    element.dataset.playerName = playerName;
    element.dataset.accountName = accountName;

    // Update sidebar
    updatePlayerActionsSidebar();
}

/**
 * Get selected players
 */
function getSelectedPlayers() {
    const selected = [];
    document.querySelectorAll('.player-item.selected').forEach(item => {
        const name = item.querySelector('.player-name').textContent;
        selected.push(name);
    });
    return selected;
}

/**
 * Update player actions sidebar with selected player info
 */
function updatePlayerActionsSidebar() {
    const selected = document.querySelector('.player-item.selected');
    const infoDiv = document.getElementById('selected-player-info');
    const bootBtn = document.getElementById('boot-player-btn');
    const banBtn = document.getElementById('ban-player-btn');

    if (selected) {
        const playerName = selected.querySelector('.player-name').textContent;
        const playerDetails = selected.querySelector('.player-details').textContent;

        infoDiv.innerHTML = `
            <p style="margin: 5px 0; font-weight: bold; color: #fff;">${playerName}</p>
            <p style="margin: 3px 0; font-size: 11px;">${playerDetails}</p>
        `;

        bootBtn.disabled = false;
        banBtn.disabled = false;
    } else {
        infoDiv.innerHTML = '<p style="margin: 0; text-align: center;">No player selected</p>';
        bootBtn.disabled = true;
        banBtn.disabled = true;
    }
}

/**
 * Boot selected player
 */
function bootSelectedPlayer() {
    const selected = document.querySelector('.player-item.selected');
    if (!selected) return;

    const playerName = selected.querySelector('.player-name').textContent;
    const confirmed = confirm(`Are you sure you want to boot ${playerName}?`);

    if (confirmed) {
        client.send('boot', ['char', playerName])
            .then(response => {
                if (response.Status === 'success') {
                    addOutput(`Successfully booted player: ${playerName}`, 'success-output');
                    // Clear selection and refresh players
                    selected.classList.remove('selected');
                    updatePlayerActionsSidebar();
                    refreshPlayers();
                } else {
                    addOutput(`Error booting player: ${response.Message}`, 'error-output');
                }
            })
            .catch(err => {
                addOutput(`Error booting player: ${err.message}`, 'error-output');
            });
    }
}

/**
 * Ban selected player
 */
function banSelectedPlayer() {
    const selected = document.querySelector('.player-item.selected');
    if (!selected) return;

    const playerName = selected.querySelector('.player-name').textContent;
    const accountName = selected.dataset.accountName;

    if (!accountName) {
        addOutput(`Error: Account name not available for player ${playerName}`, 'error-output');
        return;
    }

    const confirmed = confirm(`Are you sure you want to ban ${playerName} for 365 days? This action cannot be easily undone.`);

    if (confirmed) {
        addOutput(`Banning account: ${accountName} for 365 days...`, 'info-output');

        client.send('ban', [accountName, '365', '0', '0'])
            .then(response => {
                if (response.Status === 'success') {
                    addOutput(`Successfully banned player: ${playerName}`, 'success-output');
                    selected.classList.remove('selected');
                    updatePlayerActionsSidebar();
                    refreshPlayers();
                } else {
                    addOutput(`Error banning player: ${response.Message}`, 'error-output');
                }
            })
            .catch(err => {
                addOutput(`Error banning player: ${err.message}`, 'error-output');
            });
    }
}

/**
 * Handle incoming log messages
 */
function handleLogMessage(response) {
    // Parse message tags and apply color based on tag content
    const message = response.Message;
    let className = `${response.Status}-output`; // Default to log level color

    // Check for message tags and override color if found
    if (message.includes('[CHAT]')) {
        className = 'log-chat';
    } else if (message.includes('[AUDIT]')) {
        className = 'log-audit';
    } else if (message.includes('[SYSTEM]')) {
        className = 'log-system';
    } else if (message.includes('[WARNING]') || message.includes('[WARN]')) {
        className = 'log-warn-output';
    } else if (message.includes('[ERROR]')) {
        className = 'log-error-output';
    } else if (message.includes('[DEBUG]')) {
        className = 'log-debug-output';
    } else if (message.includes('[INFO]')) {
        className = 'log-info-output';
    }

    addOutput(message, className);
}

/**
 * Calculate uptime from a given world time (server startup time)
 */
function calculateUptime(worldTime) {
    const now = new Date();
    const uptimeTotalSeconds = Math.floor((now - worldTime) / 1000);

    const days = Math.floor(uptimeTotalSeconds / 86400);
    const hours = Math.floor((uptimeTotalSeconds % 86400) / 3600);
    const minutes = Math.floor((uptimeTotalSeconds % 3600) / 60);
    const seconds = uptimeTotalSeconds % 60;

    return `${days}d ${hours}h ${minutes}m ${seconds}s`;
}

/**
 * Handle player events (login/logoff)
 */
function handlePlayerEvent(response) {
    // Display event message
    addOutput(`[Player Event] ${response.Message}`, 'info-message');

    // Auto-refresh players list if enabled
    if (autoRefreshPlayers && client && client.isAuthenticated) {
        console.log('[UI] Auto-refreshing players due to player event');
        client.send('players', []).catch(err => console.error('[UI] Error auto-refreshing players:', err));
    }

    // Update player count and uptime if data is available
    if (response.Data) {
        if (response.Data.count !== undefined) {
            const playerEl = document.getElementById('player-count');
            if (playerEl) {
                playerEl.textContent = response.Data.count;
            }
        }

        // Update uptime from WorldTime if available
        if (response.Data.WorldTime) {
            const worldTime = new Date(response.Data.WorldTime);
            const uptime = calculateUptime(worldTime);
            const uptimeEl = document.getElementById('uptime');
            if (uptimeEl) {
                uptimeEl.textContent = uptime;
            }
        }
    }
}

/**
 * Toggle auto-refresh for players
 */
function toggleAutoRefresh(enabled) {
    autoRefreshPlayers = enabled;
    // Persist to localStorage
    localStorage.setItem('autoRefreshPlayers', enabled.toString());
    console.log('[UI] Auto-refresh players:', enabled ? 'enabled' : 'disabled');
}

/**
 * Fetch client configuration from server
 */
async function fetchClientConfig() {
    if (!client || !client.isAuthenticated) {
        console.log('[UI] Not authenticated, skipping config fetch');
        return;
    }

    try {
        const response = await client.send('config', []);
        if (response.Data) {
            clientConfig = response.Data;
            console.log('[UI] Client config received:', clientConfig);

            // Update version in footer
            if (clientConfig.Version) {
                const versionElement = document.getElementById('version');
                if (versionElement) {
                    versionElement.textContent = clientConfig.Version;
                    console.log('[UI] Version updated to:', clientConfig.Version);
                }
            }

            // Update reconnect settings in client
            if (clientConfig.MaxReconnectAttempts && clientConfig.ReconnectDelayMs) {
                client.setReconnectConfig(clientConfig.MaxReconnectAttempts, clientConfig.ReconnectDelayMs);
            }

            // If server has AutoRefreshPlayers setting and we haven't overridden it locally, use server default
            if (clientConfig.AutoRefreshPlayers !== undefined && localStorage.getItem('autoRefreshPlayers') === null) {
                autoRefreshPlayers = clientConfig.AutoRefreshPlayers;
                const checkbox = document.getElementById('auto-refresh-checkbox');
                if (checkbox) {
                    checkbox.checked = autoRefreshPlayers;
                }
                console.log('[UI] Auto-refresh set from server config:', autoRefreshPlayers);
            }

            // Populate settings in Configuration tab
            populateSettingsPanel(clientConfig);
        }
    } catch (error) {
        console.error('[UI] Error fetching client config:', error);
    }
}

/**
 * Update log message color
 */
function updateLogColor(messageType, colorValue) {
    const className = `log-${messageType}`;
    const style = document.getElementById(`log-color-style`) || createStyleElement();

    // Update the color label
    const labelSpan = document.getElementById(`color-${messageType}-label`);
    if (labelSpan) {
        labelSpan.textContent = colorValue.toUpperCase();
    }

    // Add CSS rule for the color
    let colorRule = `.${className} { color: ${colorValue} !important; }`;

    // Update existing style or create new one
    if (style.textContent.includes(`.${className}`)) {
        style.textContent = style.textContent.replace(
            new RegExp(`.${className}\\s*{[^}]*}`, 'g'),
            colorRule
        );
    } else {
        style.textContent += colorRule;
    }

    // Save to localStorage
    localStorage.setItem(`logColor_${messageType}`, colorValue);
    console.log(`[UI] Updated ${messageType} color to ${colorValue}`);
}

/**
 * Create dynamic style element for color overrides
 */
function createStyleElement() {
    const style = document.createElement('style');
    style.id = 'log-color-style';
    document.head.appendChild(style);

    // Restore saved colors
    ['chat', 'audit', 'system', 'info', 'warn', 'error', 'debug'].forEach(type => {
        const savedColor = localStorage.getItem(`logColor_${type}`);
        if (savedColor) {
            const picker = document.getElementById(`color-${type}`);
            if (picker) {
                picker.value = savedColor;
            }
            const rule = `.log-${type} { color: ${savedColor} !important; }`;
            style.textContent += rule;
        }
    });

    return style;
}

/**
 * Reset colors to default
 */
function resetColorsToDefault() {
    const defaults = {
        'chat': '#00ff00',
        'audit': '#ffff00',
        'system': '#ff00ff',
        'info': '#2196f3',
        'warn': '#ff9800',
        'error': '#f44336',
        'debug': '#888888'
    };

    Object.entries(defaults).forEach(([type, color]) => {
        const picker = document.getElementById(`color-${type}`);
        if (picker) {
            picker.value = color;
            updateLogColor(type, color);
        }
    });

    console.log('[UI] Colors reset to default');
}

/**
 * Populate settings panel with current values
 */
function populateSettingsPanel(config) {
    if (!config) return;

    // RconEnabled (read-only)
    if (config.RconEnabled !== undefined) {
        const checkbox = document.getElementById('setting-rcon-enabled');
        checkbox.checked = config.RconEnabled;
        document.getElementById('setting-rcon-enabled-current').textContent = config.RconEnabled ? 'Currently: ON' : 'Currently: OFF';
        document.getElementById('setting-rcon-enabled-current').style.color = config.RconEnabled ? '#4caf50' : '#ff9800';
    }

    // WebRconEnabled
    if (config.WebRconEnabled !== undefined) {
        const checkbox = document.getElementById('setting-web-rcon-enabled');
        checkbox.checked = config.WebRconEnabled;
        document.getElementById('setting-web-rcon-enabled-current').textContent = config.WebRconEnabled ? 'Currently: ON' : 'Currently: OFF';
        document.getElementById('setting-web-rcon-enabled-current').style.color = config.WebRconEnabled ? '#4caf50' : '#ff9800';
    }

    // MaxConnections
    if (config.MaxConnections !== undefined) {
        document.getElementById('setting-max-connections-current').textContent = `Current: ${config.MaxConnections}`;
    }

    // ConnectionTimeoutSeconds
    if (config.ConnectionTimeoutSeconds !== undefined) {
        document.getElementById('setting-connection-timeout-current').textContent = `Current: ${config.ConnectionTimeoutSeconds}s`;
    }

    // EnableLogging
    if (config.EnableLogging !== undefined) {
        const checkbox = document.getElementById('setting-enable-logging');
        checkbox.checked = config.EnableLogging;
        document.getElementById('setting-enable-logging-current').textContent = config.EnableLogging ? 'Currently: ON' : 'Currently: OFF';
        document.getElementById('setting-enable-logging-current').style.color = config.EnableLogging ? '#4caf50' : '#ff9800';
    }

    // DebugMode
    if (config.DebugMode !== undefined) {
        const checkbox = document.getElementById('setting-debug-mode');
        checkbox.checked = config.DebugMode;
        document.getElementById('setting-debug-mode-current').textContent = config.DebugMode ? 'Currently: ON' : 'Currently: OFF';
        document.getElementById('setting-debug-mode-current').style.color = config.DebugMode ? '#4caf50' : '#ff9800';
    }

    // AutoRefreshPlayers
    if (config.AutoRefreshPlayers !== undefined) {
        const checkbox = document.getElementById('setting-auto-refresh');
        checkbox.checked = config.AutoRefreshPlayers;
        document.getElementById('setting-auto-refresh-current').textContent = config.AutoRefreshPlayers ? 'Currently: ON' : 'Currently: OFF';
        document.getElementById('setting-auto-refresh-current').style.color = config.AutoRefreshPlayers ? '#4caf50' : '#ff9800';
    }

    // MaxReconnectAttempts
    if (config.MaxReconnectAttempts !== undefined) {
        document.getElementById('setting-max-reconnect-current').textContent = `Current: ${config.MaxReconnectAttempts}`;
    }

    // ReconnectDelayMs
    if (config.ReconnectDelayMs !== undefined) {
        document.getElementById('setting-reconnect-delay-current').textContent = `Current: ${config.ReconnectDelayMs}ms`;
    }

    console.log('[UI] Settings panel populated:', config);
}

/**
 * Save settings (placeholder for future API integration)
 */
function saveSettings() {
    const settings = {
        WebRconEnabled: document.getElementById('setting-web-rcon-enabled').checked,
        MaxConnections: document.getElementById('setting-max-connections').value,
        ConnectionTimeoutSeconds: document.getElementById('setting-connection-timeout').value,
        EnableLogging: document.getElementById('setting-enable-logging').checked,
        DebugMode: document.getElementById('setting-debug-mode').checked,
        AutoRefreshPlayers: document.getElementById('setting-auto-refresh').checked,
        MaxReconnectAttempts: document.getElementById('setting-max-reconnect').value,
        ReconnectDelayMs: document.getElementById('setting-reconnect-delay').value
    };

    console.log('[UI] Settings to save:', settings);
    addOutput('Settings saved! ⚠️ Server restart required to apply changes.', 'success-message');

    // TODO: Send settings to server API endpoint (when implemented)
    // This would require a new server endpoint to update settings.json
}

/**
 * Update command history dropdown
 */
function updateCommandHistoryDropdown() {
    const dropdown = document.getElementById('command-history-dropdown');
    if (!dropdown) {
        console.error('[updateCommandHistoryDropdown] Dropdown element not found!');
        return;
    }

    let mgr = historyManager;
    if (!mgr && typeof window.historyManager !== 'undefined') {
        mgr = window.historyManager;
    }

    if (!mgr || typeof mgr.getCommands !== 'function') {
        console.warn('[updateCommandHistoryDropdown] History manager not available');
        return;
    }

    const commands = mgr.getCommands();
    console.log('[updateCommandHistoryDropdown] Retrieved commands:', commands);

    // Clear existing options except the first one
    while (dropdown.options.length > 1) {
        dropdown.remove(1);
    }

    // Add commands to dropdown
    commands.forEach(command => {
        const option = document.createElement('option');
        option.value = command;
        // Truncate long commands for display
        option.textContent = command.length > 40 ? command.substring(0, 37) + '...' : command;
        dropdown.appendChild(option);
        console.log('[updateCommandHistoryDropdown] Added option:', command);
    });

    console.log('[updateCommandHistoryDropdown] Dropdown now has', dropdown.options.length, 'options');
}

/**
 * Select from command history dropdown
 */
function selectFromCommandHistory(value) {
    if (value) {
        const input = document.getElementById('command-input');
        if (input) {
            input.value = value;
            input.focus();
        }
        // Reset dropdown to default
        const dropdown = document.getElementById('command-history-dropdown');
        if (dropdown) {
            dropdown.selectedIndex = 0;
        }
    }
}

/**
 * Send world message using gamecast command
 */
async function sendWorldMessage() {
    const messageInput = document.getElementById('world-message-input');
    if (!messageInput) return;

    let message = messageInput.value.trim();
    if (!message) return;

    if (!client.isAuthenticated) {
        addOutput('Not authenticated', 'error-message');
        return;
    }

    try {
        messageInput.disabled = true;
        addOutput(`[BROADCAST] ${message}`, 'broadcast-message');

        // Parse message into words like sendCommand does
        const parts = message.split(/\s+/);
        const cmd = 'gamecast';
        const args = parts; // Message words as separate args

        console.log('[sendWorldMessage] Sending gamecast command');
        console.log('[sendWorldMessage] Command:', cmd);
        console.log('[sendWorldMessage] Args:', args);

        const response = await client.send(cmd, args);

        // Response is handled by onResponse
    } catch (error) {
        addOutput(`Broadcast error: ${error.message}`, 'error-message');
    } finally {
        messageInput.disabled = false;
        messageInput.value = '';
        messageInput.focus();

        // Add to history manager and update dropdown
        let mgr = historyManager;
        if (!mgr && typeof window.historyManager !== 'undefined') {
            mgr = window.historyManager;
        }
        if (mgr && typeof mgr.addMessage === 'function') {
            mgr.addMessage(message);
            updateMessageHistoryDropdown();
        } else {
            console.warn('[sendWorldMessage] History manager not available');
        }
    }
}

/**
 * Update message history dropdown
 */
function updateMessageHistoryDropdown() {
    const dropdown = document.getElementById('message-history-dropdown');
    if (!dropdown) return;

    let mgr = historyManager;
    if (!mgr && typeof window.historyManager !== 'undefined') {
        mgr = window.historyManager;
    }

    if (!mgr || typeof mgr.getMessages !== 'function') {
        console.warn('[updateMessageHistoryDropdown] History manager not available');
        return;
    }

    const messages = mgr.getMessages();

    // Clear existing options except the first one
    while (dropdown.options.length > 1) {
        dropdown.remove(1);
    }

    // Add messages to dropdown
    messages.forEach(message => {
        const option = document.createElement('option');
        option.value = message;
        // Truncate long messages for display
        option.textContent = message.length > 40 ? message.substring(0, 37) + '...' : message;
        dropdown.appendChild(option);
    });

    console.log('[UI] Message history dropdown updated with', messages.length, 'items');
}

/**
 * Select from message history dropdown
 */
function selectFromMessageHistory(value) {
    if (value) {
        const input = document.getElementById('world-message-input');
        if (input) {
            input.value = value;
            input.focus();
        }
        // Reset dropdown to default
        const dropdown = document.getElementById('message-history-dropdown');
        if (dropdown) {
            dropdown.selectedIndex = 0;
        }
    }
}

/**
 * Initialize history manager
 */
async function initializeHistory() {
    try {
        console.log('[UI] Initializing history manager...');

        // Ensure we have a reference to the global historyManager from history-manager.js
        if (typeof window.historyManager !== 'undefined' && window.historyManager !== null) {
            historyManager = window.historyManager;
            console.log('[UI] Assigned global historyManager from window');
        }

        console.log('[UI] historyManager type:', typeof historyManager);
        console.log('[UI] historyManager object:', historyManager);

        // historyManager is created in history-manager.js as a global
        if (!historyManager) {
            console.warn('[UI] historyManager is null or undefined - history will not work but UI will continue');
            return;  // Don't block UI initialization if history manager isn't available
        }

        console.log('[UI] Calling historyManager.init()...');
        await historyManager.init();
        console.log('[UI] historyManager.init() completed');

        // Update dropdowns after history is loaded
        try {
            updateCommandHistoryDropdown();
            updateMessageHistoryDropdown();
        } catch (e) {
            console.error('[UI] Error updating dropdowns:', e);
        }

        historyManagerReady = true;
        console.log('[UI] ✓ History manager fully initialized and ready');
    } catch (error) {
        console.error('[UI] FAILED to initialize history manager:', error);
        console.error('[UI] Error stack:', error.stack);
        // Continue anyway - history is not critical to UI function
    }
}

/**
 * ==================== BAN MANAGEMENT FUNCTIONS ====================
 */

/**
 * Fetch and display list of banned accounts
 */
async function fetchBans() {
    if (!client.isAuthenticated) {
        addOutput('Not authenticated', 'error-message');
        return;
    }

    try {
        const fetchBansBtn = document.getElementById('fetch-bans-btn');
        if (fetchBansBtn) fetchBansBtn.disabled = true;

        addOutput('> banlist', 'command-message');

        const response = await client.send('banlist', []);

        if (response.Status === 'success' && response.Data && response.Data.BannedAccounts) {
            displayBans(response.Data.BannedAccounts);
            addOutput(`Loaded ${response.Data.Count} banned accounts`, 'success-output');
        } else {
            addOutput(`Error fetching bans: ${response.Message || 'Unknown error'}`, 'error-output');
        }
    } catch (error) {
        addOutput(`Error fetching bans: ${error.message}`, 'error-output');
        console.error('[UI] Error in fetchBans:', error);
    } finally {
        const fetchBansBtn = document.getElementById('fetch-bans-btn');
        if (fetchBansBtn) fetchBansBtn.disabled = false;
    }
}

/**
 * Display list of banned accounts
 */
function displayBans(bannedAccounts) {
    const bansList = document.getElementById('bans-list');

    if (!bannedAccounts || bannedAccounts.length === 0) {
        bansList.innerHTML = '<div class="info-message">No banned accounts</div>';
        return;
    }

    console.log('[UI] displayBans called with', bannedAccounts.length, 'accounts');
    console.log('[UI] First account:', bannedAccounts[0]);

    let html = '';
    bannedAccounts.forEach((ban, index) => {
        const accountName = ban.AccountName || 'Unknown';
        const expireTime = ban.BanExpireTime || 'Unknown';
        const reason = ban.BanReason || 'No reason specified';

        console.log(`[UI] Ban ${index}: AccountName="${accountName}"`);

        html += `
            <div class="ban-item" data-account-name="${accountName}" onclick="selectBannedAccount(this)">
                <div class="ban-info">
                    <span class="ban-account-name">${accountName}</span>
                    <span class="ban-details" style="font-size: 11px; color: #aaa;">Expires: ${expireTime}</span>
                    <span class="ban-reason" style="font-size: 11px; color: #ff9800;">Reason: ${reason}</span>
                </div>
            </div>
        `;
    });

    bansList.innerHTML = html;
}

/**
 * Select a banned account and show details in sidebar
 */
async function selectBannedAccount(element) {
    console.log('[UI] selectBannedAccount called with element:', element);
    console.log('[UI] element.getAttribute(data-account-name):', element.getAttribute('data-account-name'));

    // Deselect all other bans
    document.querySelectorAll('.ban-item.selected').forEach(item => {
        item.classList.remove('selected');
    });

    // Select current ban
    element.classList.add('selected');

    // Get account name from data attribute
    const accountName = element.getAttribute('data-account-name');
    console.log('[UI] selectBannedAccount: accountName=', accountName);

    if (!accountName) {
        console.error('[UI] No account name found in element');
        return;
    }

    try {
        // Fetch detailed ban info
        const response = await client.send('baninfo', [accountName]);

        if (response.Status === 'success' && response.Data) {
            displayBanDetails(response.Data, accountName);
        } else {
            const detailsDiv = document.getElementById('ban-details-info');
            detailsDiv.innerHTML = `<p style="margin: 0; color: #f44336;">Error loading details: ${response.Message}</p>`;
        }
    } catch (error) {
        const detailsDiv = document.getElementById('ban-details-info');
        detailsDiv.innerHTML = `<p style="margin: 0; color: #f44336;">Error: ${error.message}</p>`;
        console.error('[UI] Error in selectBannedAccount:', error);
    }
}

/**
 * Display ban details in sidebar
 */
function displayBanDetails(banInfo, accountName) {
    const detailsDiv = document.getElementById('ban-details-info');
    const charactersSection = document.getElementById('ban-characters-section');
    const charactersList = document.getElementById('ban-characters-list');
    const unbanBtn = document.getElementById('unban-btn');

    // Build details HTML
    let detailsHtml = `
        <div style="margin-bottom: 10px;">
            <strong style="color: #fff;">Account:</strong> ${accountName}
        </div>
        <div style="margin-bottom: 10px;">
            <strong style="color: #fff;">Expires:</strong> ${banInfo.BanExpireTime || 'Unknown'}
        </div>
    `;

    detailsDiv.innerHTML = detailsHtml;

    // Enable buttons
    if (unbanBtn) unbanBtn.disabled = false;

    // Display characters if available
    if (banInfo.Characters && banInfo.Characters.length > 0) {
        let charsHtml = '';
        banInfo.Characters.forEach(char => {
            charsHtml += `
                <div style="margin-bottom: 8px; padding-bottom: 8px; border-bottom: 1px solid #444;">
                    <div style="color: #fff;">${char.CharacterName}</div>
                    <div style="font-size: 10px;">Level: ${char.Level} | Race: ${char.Race}</div>
                </div>
            `;
        });
        charactersList.innerHTML = charsHtml;
        if (charactersSection) charactersSection.style.display = 'block';
    } else {
        if (charactersSection) charactersSection.style.display = 'none';
    }
}

/**
 * Unban selected account
 */
async function unbanSelectedAccount() {
    const selected = document.querySelector('.ban-item.selected');
    console.log('[UI] unbanSelectedAccount: selected element=', selected);

    if (!selected) {
        console.log('[UI] unbanSelectedAccount: no selected element found');
        return;
    }

    const accountName = selected.getAttribute('data-account-name');
    console.log('[UI] unbanSelectedAccount: accountName from getAttribute=', accountName);

    if (!accountName) {
        addOutput('Error: Account name not available', 'error-output');
        console.error('[UI] unbanSelectedAccount: accountName is empty or null');
        return;
    }

    const confirmed = confirm(`Are you sure you want to unban ${accountName}? This action cannot be undone.`);

    if (confirmed) {
        try {
            addOutput(`Unbanning account: ${accountName}...`, 'info-output');

            // Use ACE's unban command via passthrough
            const response = await client.send('unban', [accountName]);

            if (response.Status === 'success') {
                addOutput(`Successfully unbanned: ${accountName}`, 'success-output');
                selected.classList.remove('selected');
                updateBanActionsSidebar();
                // Refresh the bans list
                await fetchBans();
            } else {
                addOutput(`Error unbanning: ${response.Message || 'Unknown error'}`, 'error-output');
            }
        } catch (error) {
            addOutput(`Error unbanning: ${error.message}`, 'error-output');
            console.error('[UI] Error in unbanSelectedAccount:', error);
        }
    }
}

/**
 * Update ban actions sidebar state
 */
function updateBanActionsSidebar() {
    const selected = document.querySelector('.ban-item.selected');
    const unbanBtn = document.getElementById('unban-btn');

    if (!selected) {
        if (unbanBtn) unbanBtn.disabled = true;
        const detailsDiv = document.getElementById('ban-details-info');
        if (detailsDiv) detailsDiv.innerHTML = '<p style="margin: 0; text-align: center;">No banned account selected</p>';
    }
}

console.log('[ui.js] UI module loaded');
