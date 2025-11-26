/**
 * ACE RCON Web Client UI Handler
 * Manages all UI interactions and updates
 */

let client = null;
let commandHistory = [];
let historyIndex = 0;
let autoRefreshPlayers = true;
let clientConfig = null;

/**
 * Initialize the UI and WebSocket client
 */
document.addEventListener('DOMContentLoaded', function() {
    console.log('[UI] Initializing RCON Web Client');

    // Create RCON client (use current window location)
    client = new RconClient();

    // Set up event listeners
    client.on('connected', onConnected);
    client.on('authenticated', onAuthenticated);
    client.on('response', onResponse);
    client.on('error', onError);
    client.on('disconnected', onDisconnected);

    // Set up UI event listeners
    const commandInput = document.getElementById('command-input');
    const sendBtn = document.getElementById('send-btn');

    if (sendBtn) {
        sendBtn.addEventListener('click', sendCommand);
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

    // Auto-connect on load
    setTimeout(() => {
        console.log('[UI] Auto-connecting to RCON server...');
        connectToServer();
    }, 500);
});

/**
 * Connect to RCON server
 */
async function connectToServer() {
    try {
        console.log('[UI] Connecting to server...');
        addOutput('Connecting to RCON server...', 'info-message');

        await client.connect();
        console.log('[UI] Connected to server');
    } catch (error) {
        console.error('[UI] Connection failed:', error);
        addOutput(`Connection failed: ${error.message}`, 'error-message');
    }
}

/**
 * Called when WebSocket connects
 */
function onConnected() {
    console.log('[UI] WebSocket connected');
    updateStatus('connected', 'Connected - Authenticating...');
    addOutput('Connected to RCON server. Please enter password to authenticate.', 'info-message');

    // Show password prompt
    const commandInput = document.getElementById('command-input');
    if (commandInput) {
        commandInput.placeholder = 'Enter password and press Enter...';
        commandInput.disabled = false;
        commandInput.focus();
    }
}

/**
 * Called when authenticated
 */
function onAuthenticated(authResponse) {
    console.log('[UI] Authenticated');
    updateStatus('authenticated', 'Authenticated');
    addOutput('Successfully authenticated!', 'success-message');

    // Update sidebar with status data from auth response if available
    if (authResponse && authResponse.Data) {
        updateSidebarPanel(authResponse);
    }

    // Fetch client configuration from server
    fetchClientConfig();

    // Enable UI
    enableCommands();

    // Update input placeholder
    const commandInput = document.getElementById('command-input');
    if (commandInput) {
        commandInput.placeholder = 'Enter RCON command... (e.g., status, players, help)';
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

    // Update sidebar with data if available
    if (response.Data) {
        updateSidebarPanel(response);

        // Display players in Players tab if this is a players response
        if (response.Data.players && response.Data.count !== undefined) {
            displayPlayers(response.Data);
        }
    }

    // Display response
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

    // Clear players list
    const playersList = document.getElementById('players-list');
    if (playersList) {
        playersList.innerHTML = '<div class="info-message">Players will appear here after authentication</div>';
    }

    // Reset player count
    const playerCountEl = document.getElementById('player-count');
    if (playerCountEl) {
        playerCountEl.textContent = '0';
    }

    // Reset server status
    const serverStatusEl = document.getElementById('server-status');
    if (serverStatusEl) {
        serverStatusEl.textContent = 'Disconnected';
    }

    // Reset uptime
    const uptimeEl = document.getElementById('uptime');
    if (uptimeEl) {
        uptimeEl.textContent = '0s';
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

        // Add to history
        commandHistory.push(command);
        historyIndex = commandHistory.length;
    }
}

/**
 * Send quick command
 */
async function quickCommand(cmd) {
    const commandInput = document.getElementById('command-input');
    if (commandInput) {
        commandInput.value = cmd;
    }

    // Execute it
    await sendCommand();
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

    const line = document.createElement('div');
    if (className) {
        line.className = className;
    }

    // Handle multi-line messages (like formatted JSON)
    if (message.includes('\n')) {
        const pre = document.createElement('pre');
        pre.textContent = message;
        line.appendChild(pre);
    } else {
        line.textContent = message;
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
    const quickButtons = document.querySelectorAll('.quick-commands button');
    const refreshPlayersBtn = document.getElementById('refresh-players-btn');

    if (commandInput) commandInput.disabled = false;
    if (sendBtn) sendBtn.disabled = false;
    if (refreshPlayersBtn) refreshPlayersBtn.disabled = false;

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
    const quickButtons = document.querySelectorAll('.quick-commands button');
    const refreshPlayersBtn = document.getElementById('refresh-players-btn');

    if (commandInput) commandInput.disabled = true;
    if (sendBtn) sendBtn.disabled = true;
    if (refreshPlayersBtn) refreshPlayersBtn.disabled = true;

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
        html += `
            <div class="player-item" onclick="togglePlayerSelect(this, '${player.Name}')">
                <input type="checkbox" class="player-checkbox" onclick="event.stopPropagation()">
                <div class="player-info">
                    <span class="player-name">${player.Name}</span>
                    <span class="player-level">Level: ${player.Level}</span>
                    <span class="player-location">${player.Location}</span>
                </div>
            </div>
        `;
    });

    playersList.innerHTML = html;
}

/**
 * Toggle player selection
 */
function togglePlayerSelect(element, playerName) {
    element.classList.toggle('selected');
    const checkbox = element.querySelector('.player-checkbox');
    if (checkbox) {
        checkbox.checked = !checkbox.checked;
    }
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

    // Update player count if data is available
    if (response.Data && response.Data.count !== undefined) {
        const playerEl = document.getElementById('player-count');
        if (playerEl) {
            playerEl.textContent = response.Data.count;
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

console.log('[ui.js] UI module loaded');
