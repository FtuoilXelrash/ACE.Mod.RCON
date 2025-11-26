namespace RCON;

/// <summary>
/// RCON Connection Handler
/// Manages a single client connection, handles JSON message I/O
/// </summary>
public class RconConnection
{
    private readonly int connectionId;
    private readonly Socket clientSocket;
    private readonly Settings settings;
    private byte[] receiveBuffer;
    private bool isConnected = true;

    public int ConnectionId => connectionId;
    public bool IsAuthenticated { get; set; } = false;

    public RconConnection(int connectionId, Socket clientSocket, Settings settings)
    {
        this.connectionId = connectionId;
        this.clientSocket = clientSocket;
        this.settings = settings;
        this.receiveBuffer = new byte[4096];
    }

    /// <summary>
    /// Main connection handler - receives messages and processes them
    /// </summary>
    public async Task HandleClientAsync(CancellationToken cancellationToken)
    {
        using var clientSocket = this.clientSocket;
        var networkStream = new NetworkStream(clientSocket, ownsSocket: true);

        try
        {
            // Send welcome message to telnet client
            var welcomeMsg = Encoding.UTF8.GetBytes("\r\nACE RCON Server v1.0.38\r\nSend JSON commands (one per line)\r\nExample: {\"Command\": \"auth\", \"Password\": \"your_password\", \"Identifier\": 1}\r\n\r\n");
            await networkStream.WriteAsync(welcomeMsg, 0, welcomeMsg.Length, cancellationToken);
            await networkStream.FlushAsync(cancellationToken);

            while (isConnected && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Read JSON message from client
                    var message = await ReceiveMessageAsync(networkStream, cancellationToken);

                    if (message == null)
                    {
                        // Connection closed
                        break;
                    }

                    if (settings.EnableLogging)
                        ModManager.Log($"[RCON] Connection {connectionId} received: {message}");

                    // Parse and handle the message
                    await HandleMessageAsync(message, networkStream, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    // Connection closed
                    break;
                }
                catch (Exception ex)
                {
                    ModManager.Log($"[RCON] ERROR processing message: {ex.Message}", ModManager.LogLevel.Error);

                    // Send error response
                    var errorResponse = new RconResponse
                    {
                        Identifier = -1,
                        Status = "error",
                        Message = "Internal server error"
                    };

                    try
                    {
                        await SendMessageAsync(errorResponse, networkStream, cancellationToken);
                    }
                    catch { }
                }
            }
        }
        finally
        {
            isConnected = false;
            networkStream?.Dispose();
        }
    }

    /// <summary>
    /// Receive a JSON message from the client
    /// </summary>
    private async Task<string?> ReceiveMessageAsync(NetworkStream networkStream, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        int bytesRead = 0;

        try
        {
            // Read until we get a complete JSON message followed by newline
            // Messages are delimited by \n to match telnet/console input
            // Filter out telnet protocol bytes (IAC sequences: 0xFF ...)

            while (true)
            {
                bytesRead = await networkStream.ReadAsync(receiveBuffer, 0, receiveBuffer.Length, cancellationToken);

                if (bytesRead == 0)
                {
                    // Connection closed
                    return null;
                }

                // Filter out telnet IAC (0xFF) protocol sequences
                var cleanedBytes = new List<byte>();
                int i = 0;
                while (i < bytesRead)
                {
                    if (receiveBuffer[i] == 0xFF) // Telnet IAC marker
                    {
                        // Skip this and next 2 bytes (IAC command sequence)
                        i += 3;
                    }
                    else if (receiveBuffer[i] < 32 && receiveBuffer[i] != 10 && receiveBuffer[i] != 13) // Control chars except LF/CR
                    {
                        // Skip other control characters
                        i++;
                    }
                    else
                    {
                        cleanedBytes.Add(receiveBuffer[i]);
                        i++;
                    }
                }

                if (cleanedBytes.Count > 0)
                {
                    sb.Append(Encoding.UTF8.GetString(cleanedBytes.ToArray()));
                }

                // Check if we have a complete message (terminated with newline)
                string content = sb.ToString();
                int newlineIndex = content.IndexOf('\n');
                if (newlineIndex >= 0)
                {
                    // Extract message up to newline
                    string message = content.Substring(0, newlineIndex).Trim();

                    // Keep any remaining data for next message
                    if (newlineIndex + 1 < content.Length)
                    {
                        sb.Clear();
                        sb.Append(content.Substring(newlineIndex + 1));
                    }
                    else
                    {
                        sb.Clear();
                    }

                    // Return the message (empty if only whitespace)
                    return message.Length > 0 ? message : null;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Handle an incoming RCON message
    /// </summary>
    private async Task HandleMessageAsync(string messageJson, NetworkStream networkStream, CancellationToken cancellationToken)
    {
        try
        {
            // Parse JSON message
            var message = RconProtocol.ParseMessage(messageJson);

            if (message == null)
            {
                var errorResponse = new RconResponse
                {
                    Identifier = -1,
                    Status = "error",
                    Message = "Invalid JSON message"
                };
                await SendMessageAsync(errorResponse, networkStream, cancellationToken);
                return;
            }

            // Route the command
            var response = await RconProtocol.HandleCommandAsync(message, this);

            // Send response
            await SendMessageAsync(response, networkStream, cancellationToken);
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR handling message: {ex.Message}", ModManager.LogLevel.Error);
        }
    }

    /// <summary>
    /// Send a JSON response to the client
    /// </summary>
    public async Task SendMessageAsync(RconResponse response, NetworkStream? stream = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = RconProtocol.FormatResponse(response);
            var data = Encoding.UTF8.GetBytes(json + "\n");

            if (stream != null)
            {
                await stream.WriteAsync(data, 0, data.Length, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (settings.EnableLogging)
                ModManager.Log($"[RCON] Connection {connectionId} sent: {json}");
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR sending message: {ex.Message}", ModManager.LogLevel.Error);
        }
    }

    /// <summary>
    /// Simple overload for sending without stream (for broadcast messages)
    /// </summary>
    public void SendMessage(RconResponse response)
    {
        // For now, this is a placeholder
        // Broadcast functionality to be implemented later
    }

    /// <summary>
    /// Mark connection as authenticated
    /// </summary>
    public void SetAuthenticated()
    {
        IsAuthenticated = true;
        if (settings.EnableLogging)
            ModManager.Log($"[RCON] Connection {connectionId} authenticated");
    }

    /// <summary>
    /// Disconnect the client
    /// </summary>
    public void Disconnect()
    {
        try
        {
            isConnected = false;

            if (clientSocket != null && clientSocket.Connected)
            {
                clientSocket.Shutdown(SocketShutdown.Both);
                clientSocket.Close();
            }
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR disconnecting: {ex.Message}", ModManager.LogLevel.Warn);
        }
    }
}

/// <summary>
/// RCON Request message structure
/// </summary>
public class RconRequest
{
    public string? Command { get; set; }
    public string? Password { get; set; }
    public List<string>? Args { get; set; }
    public int Identifier { get; set; }
}

/// <summary>
/// RCON Response message structure
/// </summary>
public class RconResponse
{
    public int Identifier { get; set; }
    public string? Status { get; set; }
    public string? Message { get; set; }
    public Dictionary<string, object>? Data { get; set; }
    public string? Error { get; set; }
    public bool Debug { get; set; } = false;
}
