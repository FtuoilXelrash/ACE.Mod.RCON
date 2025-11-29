namespace RCON;

/// <summary>
/// Handles real-time console log broadcasting to connected RCON clients
/// Subscribes to ModManager log events and relays to all authenticated connections
/// </summary>
public class RconLogBroadcaster
{
    private static RconLogBroadcaster? instance;
    private static readonly object lockObj = new object();

    private RconServer? rconServer;
    private RconHttpServer? httpServer;
    private Settings? settings;
    private bool isInitialized = false;

    public static RconLogBroadcaster Instance
    {
        get
        {
            if (instance == null)
            {
                lock (lockObj)
                {
                    instance ??= new RconLogBroadcaster();
                }
            }
            return instance;
        }
    }

    private RconLogBroadcaster()
    {
    }

    /// <summary>
    /// Initialize the broadcaster with references to the servers
    /// Either or both servers can be null if disabled in settings
    /// </summary>
    public void Initialize(RconServer? rconServer, RconHttpServer? httpServer, Settings settings)
    {
        lock (lockObj)
        {
            this.rconServer = rconServer;
            this.httpServer = httpServer;
            this.settings = settings;
            this.isInitialized = true;

            ModManager.Log($"[RCON] Log broadcaster initialized");
        }
    }

    /// <summary>
    /// Broadcast a console log message to all AUTHENTICATED RCON clients only
    /// Unauthenticated connections will NOT receive logs (security)
    /// </summary>
    public void BroadcastLogMessage(string message, ModManager.LogLevel logLevel = ModManager.LogLevel.Info)
    {
        if (!isInitialized || rconServer == null)
            return;

        try
        {
            // Create a log event response that clients can display
            var logResponse = new RconResponse
            {
                Identifier = 0, // 0 indicates unsolicited broadcast message
                Status = logLevel switch
                {
                    ModManager.LogLevel.Error => "log_error",
                    ModManager.LogLevel.Warn => "log_warn",
                    ModManager.LogLevel.Info => "log_info",
                    ModManager.LogLevel.Debug => "log_debug",
                    _ => "log_info"
                },
                Message = message,
                Command = "log" // Indicate this is a log event
            };

            // Broadcast ONLY to authenticated TCP connections (RconServer does auth check)
            rconServer.BroadcastMessage(logResponse);

            // Broadcast ONLY to authenticated WebSocket connections (filtering done by httpServer)
            httpServer?.BroadcastMessage(logResponse);
        }
        catch (Exception ex)
        {
            // Don't log broadcast errors to avoid infinite loops
            System.Diagnostics.Debug.WriteLine($"[RCON] Error broadcasting log: {ex.Message}");
        }
    }

    /// <summary>
    /// Broadcast a player event (join/leave)
    /// </summary>
    public void BroadcastPlayerEvent(string eventType, Dictionary<string, object> playerData)
    {
        if (!isInitialized || rconServer == null)
            return;

        try
        {
            var playerResponse = new RconResponse
            {
                Identifier = 0,
                Status = "player_event",
                Message = $"Player {eventType}",
                Command = eventType, // "login" or "logout"
                Data = playerData
            };

            if (settings?.DebugMode ?? false)
            {
                var json = JsonSerializer.Serialize(playerResponse);
                ModManager.Log($"[RCON] Broadcasting player event: {json}");
            }

            rconServer.BroadcastMessage(playerResponse);
            httpServer?.BroadcastMessage(playerResponse);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RCON] Error broadcasting player event: {ex.Message}");
        }
    }

    /// <summary>
    /// Broadcast a server status update
    /// </summary>
    public void BroadcastServerStatus(Dictionary<string, object> statusData)
    {
        if (!isInitialized || rconServer == null)
            return;

        try
        {
            var statusResponse = new RconResponse
            {
                Identifier = 0,
                Status = "status_update",
                Message = "Server status updated",
                Command = "status_update",
                Data = statusData
            };

            rconServer.BroadcastMessage(statusResponse);
            httpServer?.BroadcastMessage(statusResponse);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RCON] Error broadcasting status: {ex.Message}");
        }
    }
}
