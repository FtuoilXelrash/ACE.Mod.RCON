/**
 * ACE RCON Web Client UI Handler
 * Manages all UI interactions and updates
 */

let client = null;
let commandHistory = [];
let historyIndex = 0;

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

    // Auto-scroll to bottom
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

    if (commandInput) commandInput.disabled = false;
    if (sendBtn) sendBtn.disabled = false;

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

    if (commandInput) commandInput.disabled = true;
    if (sendBtn) sendBtn.disabled = true;

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

console.log('[ui.js] UI module loaded');
