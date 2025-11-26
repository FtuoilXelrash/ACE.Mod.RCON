namespace RCON;

/// <summary>
/// WebSocket Handler for RCON
/// Translates WebSocket messages to RCON protocol
/// </summary>
public static class RconWebSocketHandler
{
    /// <summary>
    /// Handle WebSocket connection
    /// </summary>
    public static async Task HandleWebSocketAsync(WebSocket webSocket, Settings settings)
    {
        using (webSocket)
        {
            var buffer = new byte[1024 * 4];

            try
            {
                // Create a pseudo-connection object for protocol handling
                var wsConnection = new WebSocketRconConnection(webSocket, settings);

                ModManager.Log("[RCON] WebSocket connection established");

                while (webSocket.State == WebSocketState.Open)
                {
                    try
                    {
                        // Read message from WebSocket
                        var result = await webSocket.ReceiveAsync(
                            new ArraySegment<byte>(buffer),
                            CancellationToken.None);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await webSocket.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "Connection closed",
                                CancellationToken.None);
                            break;
                        }

                        if (result.MessageType != WebSocketMessageType.Text)
                        {
                            continue;
                        }

                        // Parse JSON message
                        var messageText = Encoding.UTF8.GetString(buffer, 0, result.Count);

                        if (settings.EnableLogging)
                            ModManager.Log($"[RCON] WebSocket received: {messageText}");

                        // Parse request
                        var request = RconProtocol.ParseMessage(messageText);

                        if (request == null)
                        {
                            var errorResponse = new RconResponse
                            {
                                Identifier = -1,
                                Status = "error",
                                Message = "Invalid JSON"
                            };
                            await SendResponseAsync(webSocket, errorResponse);
                            continue;
                        }

                        // Handle command
                        var response = await RconProtocol.HandleCommandAsync(request, wsConnection);

                        // Send response back through WebSocket
                        await SendResponseAsync(webSocket, response);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        ModManager.Log($"[RCON] ERROR in WebSocket handler: {ex.Message}", ModManager.LogLevel.Error);

                        var errorResponse = new RconResponse
                        {
                            Identifier = -1,
                            Status = "error",
                            Message = "Server error"
                        };

                        try
                        {
                            await SendResponseAsync(webSocket, errorResponse);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                ModManager.Log($"[RCON] ERROR handling WebSocket: {ex.Message}", ModManager.LogLevel.Error);
            }
            finally
            {
                ModManager.Log("[RCON] WebSocket connection closed");
            }
        }
    }

    /// <summary>
    /// Send response via WebSocket
    /// </summary>
    private static async Task SendResponseAsync(WebSocket webSocket, RconResponse response)
    {
        try
        {
            // Check if WebSocket is still open
            if (webSocket.State != WebSocketState.Open)
            {
                return;
            }

            var json = RconProtocol.FormatResponse(response);
            var data = Encoding.UTF8.GetBytes(json);

            await webSocket.SendAsync(
                new ArraySegment<byte>(data),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None);
        }
        catch (ObjectDisposedException)
        {
            // Socket was disposed, ignore
        }
        catch (WebSocketException)
        {
            // WebSocket closed unexpectedly, ignore
        }
        catch (Exception ex)
        {
            if (!ex.Message.Contains("invalid state") && !ex.Message.Contains("Aborted"))
            {
                ModManager.Log($"[RCON] ERROR sending WebSocket response: {ex.Message}", ModManager.LogLevel.Error);
            }
        }
    }
}

/// <summary>
/// Pseudo-connection for WebSocket clients
/// Allows WebSocket clients to work with RconProtocol
/// </summary>
public class WebSocketRconConnection : RconConnection
{
    private readonly WebSocket webSocket;

    public WebSocketRconConnection(WebSocket webSocket, Settings settings)
        : base(
            connectionId: System.Threading.Interlocked.Increment(ref connectionIdCounter),
            clientSocket: null!,
            settings: settings)
    {
        this.webSocket = webSocket;
    }

    private static int connectionIdCounter = 1000;

    public void SendMessage(RconResponse response)
    {
        // WebSocket responses are handled by the handler
        // This is for broadcast messages (Phase 2+)
    }
}
