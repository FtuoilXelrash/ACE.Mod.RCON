/**
 * ACE RCON WebSocket Client Library
 * Communicates with RCON server via WebSocket
 *
 * Usage:
 * const client = new RconClient('localhost', 2948);
 * await client.connect();
 * await client.authenticate('your_password');
 * const response = await client.send('status');
 * client.on('response', (data) => { console.log(data); });
 */

class RconClient {
    constructor(host = null, port = null) {
        // Use current window location if not specified
        this.host = host || window.location.hostname;
        this.port = port || 9005;  // Default to RCON WebSocket port
        this.ws = null;
        this.isConnected = false;
        this.isAuthenticated = false;
        this.requestId = 0;
        this.pendingRequests = new Map();
        this.listeners = new Map();
        this.reconnectDelay = 15000;  // Will be updated from server config
        this.reconnectAttempts = 0;
        this.maxReconnectAttempts = 42;  // Will be updated from server config
        this.password = null;  // For Rust-style URL auth
        this.disableReconnect = false;  // Flag to disable auto-reconnect after successful auth
        this.reconnectTimer = null;  // Timer ID for scheduled reconnection
    }

    /**
     * Set reconnect configuration from server
     */
    setReconnectConfig(maxAttempts, delayMs) {
        this.maxReconnectAttempts = maxAttempts || 42;
        this.reconnectDelay = delayMs || 15000;
        console.log(`[RconClient] Reconnect config updated: ${this.maxReconnectAttempts} attempts, ${this.reconnectDelay}ms delay`);
    }

    /**
     * Set password for Rust-style URL authentication
     */
    setPassword(password) {
        this.password = password;
        console.log('[RconClient] Password set for URL authentication');
    }

    /**
     * Fetch server configuration (no auth required)
     * @returns {Promise} Resolves with config data
     */
    async getConfig() {
        return new Promise((resolve, reject) => {
            if (!this.isConnected) {
                reject(new Error('Not connected to server'));
                return;
            }

            const requestId = ++this.requestId;
            const message = {
                Command: 'config',
                Identifier: requestId
            };

            console.log('[RconClient] Fetching server config...');

            try {
                const timeout = setTimeout(() => {
                    this.pendingRequests.delete(requestId);
                    reject(new Error('Config request timeout'));
                }, 10000);

                this.pendingRequests.set(requestId, {
                    resolve: (response) => {
                        if (response.Status === 'success') {
                            this.emit('server-config', response.Data);
                            resolve(response.Data);
                        } else {
                            reject(new Error(response.Message || 'Failed to get config'));
                        }
                    },
                    reject,
                    timeout
                });

                this.ws.send(JSON.stringify(message));
            } catch (error) {
                this.pendingRequests.delete(requestId);
                reject(error);
            }
        });
    }

    /**
     * Connect to RCON server
     * @returns {Promise} Resolves when connected
     */
    connect() {
        return new Promise((resolve, reject) => {
            try {
                // For Rust-style auth, include password in URL path
                let wsUrl = `ws://${this.host}:${this.port}`;
                if (this.password) {
                    wsUrl += `/${this.password}`;
                } else {
                    wsUrl += `/rcon`;  // Default path for ACE auth mode
                }
                console.log(`[RconClient] Connecting to ${wsUrl.replace(this.password || '', '***')}`);

                this.ws = new WebSocket(wsUrl);

                this.ws.onopen = () => {
                    console.log('[RconClient] Connected');
                    this.isConnected = true;
                    this.reconnectAttempts = 0;
                    this.emit('connected');
                    resolve();
                };

                this.ws.onmessage = (event) => {
                    try {
                        const response = JSON.parse(event.data);
                        console.log('[RconClient] Received:', response);

                        // Check if this matches a pending request
                        const requestId = response.Identifier;
                        if (this.pendingRequests.has(requestId)) {
                            const resolver = this.pendingRequests.get(requestId);
                            this.pendingRequests.delete(requestId);
                            clearTimeout(resolver.timeout);
                            resolver.resolve(response);
                        }

                        // Also emit general response event
                        this.emit('response', response);
                    } catch (error) {
                        console.error('[RconClient] Error parsing message:', error);
                    }
                };

                this.ws.onerror = (event) => {
                    console.error('[RconClient] WebSocket error:', event);
                    this.emit('error', event);
                };

                this.ws.onclose = (event) => {
                    console.log('[RconClient] Disconnected', event);
                    this.isConnected = false;
                    this.isAuthenticated = false;
                    this.emit('disconnected');

                    // Check if connection was rejected due to auth failure (PolicyViolation close code = 1008)
                    const isAuthFailure = event.code === 1008; // WebSocketCloseStatus.PolicyViolation

                    // Reject any pending requests if auth failed
                    if (isAuthFailure) {
                        const pendingError = new Error('Connection rejected - Invalid credentials');
                        this.pendingRequests.forEach((request) => {
                            request.reject(pendingError);
                        });
                        this.pendingRequests.clear();
                    }

                    // Don't auto-reconnect if reconnect is disabled (after successful auth)
                    if (this.disableReconnect) {
                        console.log('[RconClient] Auto-reconnect is disabled - not reconnecting');
                        if (this.reconnectTimer) {
                            clearTimeout(this.reconnectTimer);
                            this.reconnectTimer = null;
                        }
                        return;
                    }

                    // Try to reconnect (but not for auth failures on initial connection from connect() call)
                    // Auth failures are detected by the caller
                    if (this.reconnectAttempts < this.maxReconnectAttempts && !isAuthFailure) {
                        this.reconnectAttempts++;
                        console.log(`[RconClient] Attempting to reconnect (${this.reconnectAttempts}/${this.maxReconnectAttempts})...`);
                        this.reconnectTimer = setTimeout(() => {
                            this.reconnectTimer = null;
                            this.connect().catch(err => console.error('[RconClient] Reconnect failed:', err));
                        }, this.reconnectDelay);
                    } else if (isAuthFailure) {
                        console.log('[RconClient] Connection rejected due to auth failure');
                        reject(new Error('Connection rejected - Invalid credentials'));
                    } else {
                        console.log('[RconClient] Max reconnect attempts reached');
                        reject(new Error('Connection failed'));
                    }
                };
            } catch (error) {
                console.error('[RconClient] Connection error:', error);
                reject(error);
            }
        });
    }

    /**
     * Authenticate with RCON server
     * @param {string} password - RCON password
     * @returns {Promise} Resolves when authenticated
     */
    authenticate(password, accountName = null) {
        return new Promise((resolve, reject) => {
            if (!this.isConnected) {
                reject(new Error('Not connected to server'));
                return;
            }

            const requestId = ++this.requestId;
            const message = {
                Command: 'auth',
                Password: password,
                Identifier: requestId
            };

            // For ACE-style auth, send account name as Name field
            if (accountName) {
                message.Name = accountName;
            }

            console.log('[RconClient] Authenticating...');

            try {
                const timeout = setTimeout(() => {
                    this.pendingRequests.delete(requestId);
                    reject(new Error('Authentication timeout'));
                }, 30000);

                this.pendingRequests.set(requestId, {
                    resolve: (response) => {
                        if (response.Status === 'authenticated' || response.Status === 'success') {
                            this.isAuthenticated = true;
                            // Disable auto-reconnect after successful authentication
                            this.disableReconnect = true;
                            this.reconnectAttempts = 0;
                            // Clear any pending reconnection timer since we're authenticated now
                            if (this.reconnectTimer) {
                                clearTimeout(this.reconnectTimer);
                                this.reconnectTimer = null;
                            }
                            this.emit('authenticated', response);
                            resolve(response);
                        } else {
                            reject(new Error(response.Message || 'Authentication failed'));
                        }
                    },
                    reject,
                    timeout
                });

                this.ws.send(JSON.stringify(message));
            } catch (error) {
                this.pendingRequests.delete(requestId);
                reject(error);
            }
        });
    }

    /**
     * Send RCON command
     * @param {string} command - Command name
     * @param {Array} args - Command arguments
     * @returns {Promise} Resolves with response, rejects if command fails
     */
    send(command, args = []) {
        return new Promise((resolve, reject) => {
            if (!this.isConnected) {
                reject(new Error('Not connected to server'));
                return;
            }

            const requestId = ++this.requestId;
            const message = {
                Command: command,
                Args: args,
                Identifier: requestId
            };

            console.log('[RconClient] Sending:', message);

            try {
                // Set up timeout
                const timeout = setTimeout(() => {
                    this.pendingRequests.delete(requestId);
                    reject(new Error('Command timeout'));
                }, 30000);

                // Store pending request with status-checking resolver
                this.pendingRequests.set(requestId, {
                    resolve: (response) => {
                        // Reject if server returned an error status
                        if (response.Status === 'error') {
                            reject(new Error(response.Message || 'Command failed'));
                        } else {
                            // Check if this is an authentication response
                            if (response.Status === 'authenticated' || response.Status === 'success') {
                                // Check if we just authenticated (not already authenticated)
                                if (!this.isAuthenticated && (command === 'hello' || command === 'auth')) {
                                    this.isAuthenticated = true;
                                    // Disable auto-reconnect after successful authentication
                                    this.disableReconnect = true;
                                    this.reconnectAttempts = 0;
                                    // Clear any pending reconnection timer since we're authenticated now
                                    if (this.reconnectTimer) {
                                        clearTimeout(this.reconnectTimer);
                                        this.reconnectTimer = null;
                                    }
                                    this.emit('authenticated', response);
                                }
                            }
                            // Resolve for success, authenticated, or other non-error statuses
                            resolve(response);
                        }
                    },
                    reject,
                    timeout
                });

                // Send message
                this.ws.send(JSON.stringify(message));
            } catch (error) {
                this.pendingRequests.delete(requestId);
                reject(error);
            }
        });
    }

    /**
     * Register event listener
     * @param {string} event - Event name (connected, authenticated, response, error, disconnected)
     * @param {Function} callback - Callback function
     */
    on(event, callback) {
        if (!this.listeners.has(event)) {
            this.listeners.set(event, []);
        }
        this.listeners.get(event).push(callback);
        console.log(`[RconClient] Event listener registered: ${event}`);
    }

    /**
     * Remove event listener
     * @param {string} event - Event name
     * @param {Function} callback - Callback function
     */
    off(event, callback) {
        if (!this.listeners.has(event)) return;
        const listeners = this.listeners.get(event);
        const index = listeners.indexOf(callback);
        if (index > -1) {
            listeners.splice(index, 1);
        }
    }

    /**
     * Emit event
     * @param {string} event - Event name
     * @param {any} data - Event data
     */
    emit(event, data) {
        if (!this.listeners.has(event)) return;
        const listeners = this.listeners.get(event);
        listeners.forEach(callback => {
            try {
                callback(data);
            } catch (error) {
                console.error(`[RconClient] Error in ${event} listener:`, error);
            }
        });
    }

    /**
     * Disconnect from server
     */
    disconnect() {
        console.log('[RconClient] Disconnecting...');
        // Clear any pending reconnection timer
        if (this.reconnectTimer) {
            clearTimeout(this.reconnectTimer);
            this.reconnectTimer = null;
        }
        if (this.ws) {
            this.ws.close();
            this.ws = null;
        }
        this.isConnected = false;
        this.isAuthenticated = false;
        this.disableReconnect = false;  // Reset reconnect flag on disconnect
    }

    /**
     * Get connection status
     * @returns {Object} Status object
     */
    getStatus() {
        return {
            isConnected: this.isConnected,
            isAuthenticated: this.isAuthenticated,
            pendingRequests: this.pendingRequests.size,
            requestId: this.requestId
        };
    }
}

// Export for use in HTML
if (typeof module !== 'undefined' && module.exports) {
    module.exports = RconClient;
}

console.log('[rcon-client.js] RconClient library loaded');
