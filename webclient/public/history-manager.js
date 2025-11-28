/**
 * History Manager for RCON Commands and World Messages
 * Manages persistent storage of command/message history in browser and JSON files
 */

class HistoryManager {
    constructor() {
        this.commandHistory = [];
        this.messageHistory = [];
        this.maxHistorySize = 10;
    }

    /**
     * Initialize history from browser storage
     */
    async init() {
        // Load from localStorage
        const savedCommands = localStorage.getItem('rconCommandHistory');
        const savedMessages = localStorage.getItem('rconMessageHistory');

        if (savedCommands) {
            try {
                this.commandHistory = JSON.parse(savedCommands);
                console.log('[HistoryManager] Loaded command history:', this.commandHistory);
            } catch (e) {
                console.error('[HistoryManager] Failed to parse command history:', e);
                this.commandHistory = [];
            }
        }

        if (savedMessages) {
            try {
                this.messageHistory = JSON.parse(savedMessages);
                console.log('[HistoryManager] Loaded message history:', this.messageHistory);
            } catch (e) {
                console.error('[HistoryManager] Failed to parse message history:', e);
                this.messageHistory = [];
            }
        }

        // Sync with server-side JSON files if available
        await this.syncWithServer();
    }

    /**
     * Sync history with server JSON files
     */
    async syncWithServer() {
        try {
            // Try to fetch existing history from server
            const response = await fetch('/config/history.json');
            if (response.ok) {
                const data = await response.json();
                if (data.commands && Array.isArray(data.commands)) {
                    this.commandHistory = data.commands.slice(-this.maxHistorySize);
                }
                if (data.messages && Array.isArray(data.messages)) {
                    this.messageHistory = data.messages.slice(-this.maxHistorySize);
                }
                console.log('[HistoryManager] Synced history from server');
            }
        } catch (e) {
            console.log('[HistoryManager] Server history not available yet (will be created on first use)');
        }
    }

    /**
     * Add command to history and save
     */
    addCommand(command) {
        if (!command || command.trim() === '') return;

        // Remove if already exists (avoid duplicates, move to front)
        this.commandHistory = this.commandHistory.filter(c => c !== command);

        // Add to front
        this.commandHistory.unshift(command);

        // Keep only last 10
        this.commandHistory = this.commandHistory.slice(0, this.maxHistorySize);

        this.saveHistory();
        console.log('[HistoryManager] Command added:', command, 'Total:', this.commandHistory.length);
        return this.commandHistory;
    }

    /**
     * Add message to history and save
     */
    addMessage(message) {
        if (!message || message.trim() === '') return;

        // Remove if already exists (avoid duplicates, move to front)
        this.messageHistory = this.messageHistory.filter(m => m !== message);

        // Add to front
        this.messageHistory.unshift(message);

        // Keep only last 10
        this.messageHistory = this.messageHistory.slice(0, this.maxHistorySize);

        this.saveHistory();
        console.log('[HistoryManager] Message added:', message, 'Total:', this.messageHistory.length);
        return this.messageHistory;
    }

    /**
     * Save history to localStorage and trigger server save
     */
    saveHistory() {
        try {
            // Save to localStorage
            const cmdJson = JSON.stringify(this.commandHistory);
            const msgJson = JSON.stringify(this.messageHistory);
            localStorage.setItem('rconCommandHistory', cmdJson);
            localStorage.setItem('rconMessageHistory', msgJson);
            console.log('[HistoryManager] SAVED to localStorage - Commands:', this.commandHistory.length, 'Messages:', this.messageHistory.length);
            console.log('[HistoryManager] Command history:', this.commandHistory);
            console.log('[HistoryManager] Message history:', this.messageHistory);
        } catch (e) {
            console.error('[HistoryManager] FAILED to save to localStorage:', e);
        }

        // Try to sync with server (fire and forget)
        this.syncToServer().catch(err =>
            console.log('[HistoryManager] Server sync failed (continuing):', err)
        );
    }

    /**
     * Send history to server for persistent JSON storage
     */
    async syncToServer() {
        try {
            const response = await fetch('/api/save-history', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({
                    commands: this.commandHistory,
                    messages: this.messageHistory
                })
            });

            if (!response.ok) {
                console.log('[HistoryManager] Server sync response:', response.status);
            }
        } catch (e) {
            // Server sync is optional - continue without it
            console.log('[HistoryManager] Server sync not available');
        }
    }

    /**
     * Get all commands for dropdown
     */
    getCommands() {
        return this.commandHistory;
    }

    /**
     * Get all messages for dropdown
     */
    getMessages() {
        return this.messageHistory;
    }

    /**
     * Clear all history
     */
    clearHistory() {
        this.commandHistory = [];
        this.messageHistory = [];
        localStorage.removeItem('rconCommandHistory');
        localStorage.removeItem('rconMessageHistory');
    }
}

// Create global instance
const historyManager = new HistoryManager();

console.log('[history-manager.js] HistoryManager loaded');
