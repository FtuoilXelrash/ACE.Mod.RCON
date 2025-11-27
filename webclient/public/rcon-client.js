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

                    // Try to reconnect (but not for auth failures on initial connection from connect() call)
                    // Auth failures are detected by the caller
                    if (this.reconnectAttempts < this.maxReconnectAttempts && !isAuthFailure) {
                        this.reconnectAttempts++;
                        console.log(`[RconClient] Attempting to reconnect (${this.reconnectAttempts}/${this.maxReconnectAttempts})...`);
                        setTimeout(() => {
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
    authenticate(password) {
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
     * @returns {Promise} Resolves with response
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

                // Store pending request
                this.pendingRequests.set(requestId, {
                    resolve,
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
        if (this.ws) {
            this.ws.close();
            this.ws = null;
        }
        this.isConnected = false;
        this.isAuthenticated = false;
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
