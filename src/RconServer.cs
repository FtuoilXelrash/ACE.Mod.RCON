namespace RCON;

/// <summary>
/// RCON TCP Server
/// Listens for client connections and manages multiple concurrent RCON sessions
/// </summary>
public class RconServer
{
    private readonly Settings settings;
    private TcpListener? tcpListener;
    private readonly ConcurrentDictionary<int, RconConnection> activeConnections;
    private int connectionIdCounter = 0;
    private CancellationTokenSource? cancellationTokenSource;
    private Task? acceptClientsTask;

    public RconServer(Settings settings)
    {
        this.settings = settings;
        this.activeConnections = new ConcurrentDictionary<int, RconConnection>();
    }

    /// <summary>
    /// Start the RCON server and begin accepting connections
    /// </summary>
    public void Start()
    {
        try
        {
            cancellationTokenSource = new CancellationTokenSource();

            // Create TCP listener
            tcpListener = new TcpListener(IPAddress.Loopback, settings.RconPort);
            tcpListener.Start(settings.MaxConnections);

            ModManager.Log($"[RCON] TCP listener started on port {settings.RconPort}");

            // Start accepting clients asynchronously
            acceptClientsTask = AcceptClientsAsync(cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR: Failed to start server - {ex.Message}", ModManager.LogLevel.Error);
            throw;
        }
    }

    /// <summary>
    /// Stop the RCON server and close all client connections
    /// </summary>
    public void Stop()
    {
        try
        {
            ModManager.Log($"[RCON] Stopping server...");

            // Cancel the accept loop
            cancellationTokenSource?.Cancel();

            // Close TCP listener
            tcpListener?.Stop();
            tcpListener?.Dispose();
            tcpListener = null;

            // Close all active connections
            foreach (var kvp in activeConnections)
            {
                try
                {
                    kvp.Value.Disconnect();
                }
                catch (Exception ex)
                {
                    ModManager.Log($"[RCON] ERROR closing connection {kvp.Key}: {ex.Message}", ModManager.LogLevel.Warn);
                }
            }

            activeConnections.Clear();

            // Wait for accept task to complete
            if (acceptClientsTask != null && !acceptClientsTask.IsCompleted)
            {
                acceptClientsTask.Wait(TimeSpan.FromSeconds(5));
            }

            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;

            ModManager.Log($"[RCON] Server stopped");
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR during stop: {ex.Message}", ModManager.LogLevel.Error);
        }
    }

    /// <summary>
    /// Accept incoming client connections in a loop
    /// </summary>
    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (tcpListener == null)
                    break;

                // Accept incoming connection
                var clientSocket = await tcpListener.AcceptSocketAsync(cancellationToken);

                // Check if we've reached max connections
                if (activeConnections.Count >= settings.MaxConnections)
                {
                    ModManager.Log($"[RCON] Maximum connections ({settings.MaxConnections}) reached, rejecting new connection");
                    clientSocket.Close();
                    clientSocket.Dispose();
                    continue;
                }

                // Create connection handler
                int connectionId = Interlocked.Increment(ref connectionIdCounter);
                var connection = new RconConnection(connectionId, clientSocket, settings);

                // Add to active connections
                activeConnections.TryAdd(connectionId, connection);

                if (settings.EnableLogging)
                    ModManager.Log($"[RCON] New connection accepted (ID: {connectionId}, Total: {activeConnections.Count})");

                // Handle connection asynchronously
                _ = HandleClientAsync(connectionId, connection, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when shutting down
                break;
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    ModManager.Log($"[RCON] ERROR accepting client: {ex.Message}", ModManager.LogLevel.Error);
                }
            }
        }
    }

    /// <summary>
    /// Handle an individual client connection
    /// </summary>
    private async Task HandleClientAsync(int connectionId, RconConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await connection.HandleClientAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR in connection {connectionId}: {ex.Message}", ModManager.LogLevel.Error);
        }
        finally
        {
            // Remove from active connections and cleanup
            activeConnections.TryRemove(connectionId, out _);
            connection.Disconnect();

            if (settings.EnableLogging)
                ModManager.Log($"[RCON] Connection closed (ID: {connectionId}, Remaining: {activeConnections.Count})");
        }
    }

    /// <summary>
    /// Broadcast a message to all authenticated connections
    /// </summary>
    public void BroadcastMessage(RconResponse message)
    {
        foreach (var connection in activeConnections.Values)
        {
            try
            {
                if (connection.IsAuthenticated)
                {
                    connection.SendMessage(message);
                }
            }
            catch (Exception ex)
            {
                if (settings.EnableLogging)
                    ModManager.Log($"[RCON] ERROR broadcasting message: {ex.Message}", ModManager.LogLevel.Warn);
            }
        }
    }

    /// <summary>
    /// Get current connection statistics
    /// </summary>
    public RconServerStats GetStats()
    {
        return new RconServerStats
        {
            IsRunning = tcpListener != null,
            ActiveConnections = activeConnections.Count,
            MaxConnections = settings.MaxConnections,
            Port = settings.RconPort,
            AuthenticatedConnections = activeConnections.Values.Count(c => c.IsAuthenticated)
        };
    }
}

/// <summary>
/// Server statistics
/// </summary>
public class RconServerStats
{
    public bool IsRunning { get; set; }
    public int ActiveConnections { get; set; }
    public int AuthenticatedConnections { get; set; }
    public int MaxConnections { get; set; }
    public int Port { get; set; }
}
