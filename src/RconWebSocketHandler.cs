namespace RCON;

/// <summary>
/// WebSocket Handler for RCON
/// Translates WebSocket messages to RCON protocol
/// </summary>
public static class RconWebSocketHandler
{
    private static RconHttpServer? httpServer;

    public static void Initialize(RconHttpServer server)
    {
        httpServer = server;
    }

    /// <summary>
    /// Handle WebSocket connection
    /// </summary>
    public static async Task HandleWebSocketAsync(WebSocket webSocket, Settings settings, TcpClient? client = null, NetworkStream? stream = null)
    {
        using (webSocket)
        {
            var buffer = new byte[1024 * 4];

            try
            {
                // Register WebSocket for broadcasting
                httpServer?.RegisterWebSocket(webSocket);

                // Create a pseudo-connection object for protocol handling
                var wsConnection = new WebSocketRconConnection(webSocket, settings);

                ModManager.Log("[RCON] WebSocket connection established");

                int loopCount = 0;
                while (webSocket.State == WebSocketState.Open && loopCount < 100)
                {
                    loopCount++;
                    try
                    {
                        ModManager.Log($"[RCON] WebSocket loop iteration {loopCount}, state: {webSocket.State}, CloseStatus: {webSocket.CloseStatus}", ModManager.LogLevel.Info);
                        // Read message from WebSocket - no timeout to allow idle connections
                        var result = await webSocket.ReceiveAsync(
                            new ArraySegment<byte>(buffer),
                            CancellationToken.None);

                        ModManager.Log($"[RCON] WebSocket received data, type: {result.MessageType}, count: {result.Count}, EndOfMessage: {result.EndOfMessage}", ModManager.LogLevel.Info);

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
                        var response = await RconProtocol.HandleCommandAsync(request, wsConnection, settings);

                        // Track authentication status for broadcast filtering
                        if (wsConnection.IsAuthenticated && httpServer != null)
                        {
                            httpServer.SetWebSocketAuthenticated(webSocket, true);
                        }

                        // Log response data if present
                        if (response.Data != null)
                        {
                            ModManager.Log($"[RCON] Response has Data object with {response.Data.Count} fields", ModManager.LogLevel.Info);
                        }

                        // Send response back through WebSocket
                        await SendResponseAsync(webSocket, response);
                    }
                    catch (WebSocketException wex)
                    {
                        // WebSocket was closed or connection lost
                        if (wex.Message.Contains("closed") || wex.Message.Contains("Aborted") || wex.Message.Contains("invalid state"))
                        {
                            // Normal shutdown, don't log as error
                            ModManager.Log($"[RCON] WebSocket closed normally: {wex.Message}", ModManager.LogLevel.Info);
                            break;
                        }
                        ModManager.Log($"[RCON] WebSocket error: {wex.Message} | Stack: {wex.StackTrace}", ModManager.LogLevel.Error);
                        break;
                    }
                    catch (Exception ex)
                    {
                        // Don't log errors during shutdown
                        if (!ex.Message.Contains("Aborted") && !ex.Message.Contains("invalid state") && !ex.Message.Contains("closed"))
                        {
                            ModManager.Log($"[RCON] ERROR in WebSocket handler: {ex.Message} | Stack: {ex.StackTrace}", ModManager.LogLevel.Error);
                        }
                        else
                        {
                            ModManager.Log($"[RCON] WebSocket closed: {ex.Message}", ModManager.LogLevel.Info);
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                ModManager.Log($"[RCON] ERROR handling WebSocket: {ex.Message} | Stack: {ex.StackTrace}", ModManager.LogLevel.Error);
            }
            finally
            {
                // Unregister WebSocket from broadcasting
                httpServer?.UnregisterWebSocket(webSocket);

                ModManager.Log("[RCON] WebSocket connection closed");
            }
        }

        // Clean up TCP resources after WebSocket is disposed
        try { stream?.Dispose(); } catch { }
        try { client?.Dispose(); } catch { }
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
