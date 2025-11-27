namespace RCON;

using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// WebSocket connection entry tracking socket and auth status
/// </summary>
public class WebSocketEntry
{
    public WebSocket WebSocket { get; set; }
    public bool IsAuthenticated { get; set; }

    public WebSocketEntry(WebSocket ws)
    {
        WebSocket = ws;
        IsAuthenticated = false;
    }
}

/// <summary>
/// HTTP/WebSocket Server using raw TcpListener
/// Unlike HttpListener, TcpListener can bind to all interfaces without requiring admin privileges
/// </summary>
public class RconHttpServer
{
    private readonly Settings settings;
    private TcpListener? tcpListener;
    private CancellationTokenSource? cancellationTokenSource;
    private Task? listenTask;
    private readonly ConcurrentDictionary<int, WebSocketEntry> activeWebSockets;
    private int webSocketIdCounter = 0;
    private const int PORT = 9005;

    public RconHttpServer(Settings settings)
    {
        this.settings = settings;
        this.activeWebSockets = new ConcurrentDictionary<int, WebSocketEntry>();
    }

    /// <summary>
    /// Start the HTTP/WebSocket server
    /// </summary>
    public void Start()
    {
        try
        {
            // Bind to all interfaces on port 9005
            tcpListener = new TcpListener(IPAddress.Any, PORT);
            tcpListener.Start();

            cancellationTokenSource = new CancellationTokenSource();

            ModManager.Log($"[RCON] Web server started on 0.0.0.0:{PORT}/");
            ModManager.Log($"[RCON] Access at http://localhost:{PORT}/ or http://<server-ip>:{PORT}/");
            ModManager.Log($"[RCON] WebSocket endpoint: ws://localhost:{PORT}/rcon or ws://<server-ip>:{PORT}/rcon");

            // Start accepting connections in background
            listenTask = AcceptConnectionsAsync(cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR starting web server: {ex.Message}", ModManager.LogLevel.Error);
            throw;
        }
    }

    /// <summary>
    /// Stop the HTTP/WebSocket server
    /// </summary>
    public void Stop()
    {
        try
        {
            cancellationTokenSource?.Cancel();
            tcpListener?.Stop();
            tcpListener = null;

            if (listenTask != null && !listenTask.IsCompleted)
            {
                listenTask.Wait(TimeSpan.FromSeconds(5));
            }

            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;

            ModManager.Log($"[RCON] Web server stopped");
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR stopping web server: {ex.Message}", ModManager.LogLevel.Error);
        }
    }

    /// <summary>
    /// Accept incoming TCP connections
    /// </summary>
    private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && tcpListener != null)
        {
            try
            {
                var client = await tcpListener.AcceptTcpClientAsync(cancellationToken);
                _ = HandleClientAsync(client, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    ModManager.Log($"[RCON] ERROR accepting connection: {ex.Message}", ModManager.LogLevel.Error);
                }
            }
        }
    }

    /// <summary>
    /// Handle a TCP client connection
    /// </summary>
    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();

                // Read HTTP request
                var request = await ReadHttpRequestAsync(stream, cancellationToken);

                if (request == null)
                {
                    return;
                }

                if (settings.EnableLogging)
                    ModManager.Log($"[RCON] HTTP {request.Method} {request.Path}");

                // Check if this is a WebSocket upgrade request (on any path)
                if (request.IsWebSocketUpgrade)
                {
                    if (settings.EnableLogging)
                        ModManager.Log($"[RCON] WebSocket upgrade request detected on {request.Path}");

                    await HandleWebSocketAsync(stream, request, cancellationToken);
                }
                else
                {
                    // Handle regular HTTP request for static files
                    await HandleHttpRequestAsync(stream, request, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                ModManager.Log($"[RCON] ERROR handling client: {ex.Message}", ModManager.LogLevel.Error);
            }
        }
    }

    /// <summary>
    /// Read HTTP request from stream
    /// </summary>
    private async Task<HttpRequest?> ReadHttpRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

        if (bytesRead == 0)
            return null;

        var requestText = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        var lines = requestText.Split("\r\n");

        if (lines.Length == 0)
            return null;

        // Parse request line
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 3)
            return null;

        var request = new HttpRequest
        {
            Method = requestLine[0],
            Path = requestLine[1].Split('?')[0],
            Version = requestLine[2]
        };

        // Parse headers
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                break;

            var headerParts = lines[i].Split(':', 2);
            if (headerParts.Length == 2)
            {
                headers[headerParts[0].Trim()] = headerParts[1].Trim();
            }
        }

        request.Headers = headers;

        // Check for WebSocket upgrade
        request.IsWebSocketUpgrade = headers.ContainsKey("Upgrade") &&
            headers["Upgrade"].Equals("websocket", StringComparison.OrdinalIgnoreCase) &&
            headers.ContainsKey("Connection") &&
            headers["Connection"].Contains("Upgrade", StringComparison.OrdinalIgnoreCase);

        return request;
    }

    /// <summary>
    /// Handle WebSocket upgrade request
    /// </summary>
    private async Task HandleWebSocketAsync(NetworkStream stream, HttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var headers = request.Headers;

            // Get required WebSocket headers
            if (!headers.TryGetValue("Sec-WebSocket-Key", out var secKey))
            {
                await SendHttpResponseAsync(stream, 400, "Bad Request");
                return;
            }

            if (!headers.TryGetValue("Sec-WebSocket-Version", out var version) || version != "13")
            {
                await SendHttpResponseAsync(stream, 400, "Bad Request");
                return;
            }

            // Calculate Sec-WebSocket-Accept
            var sha1 = SHA1.Create();
            var acceptKey = Convert.ToBase64String(
                sha1.ComputeHash(Encoding.UTF8.GetBytes(secKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"))
            );

            // Send WebSocket upgrade response
            var upgradeResponse = "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Accept: {acceptKey}\r\n" +
                "\r\n";

            await stream.WriteAsync(Encoding.UTF8.GetBytes(upgradeResponse), 0, upgradeResponse.Length);
            await stream.FlushAsync();

            // Wrap stream in WebSocket
            var webSocket = WebSocket.CreateFromStream(stream, true, "websocket", TimeSpan.FromSeconds(300));

            ModManager.Log($"[RCON] WebSocket connection established");

            // Register connection
            RegisterWebSocket(webSocket);

            // Create pseudo-connection for this WebSocket
            var wsConnection = new WebSocketRconConnection(webSocket, settings);

            // Handle Rust-style URL-based authentication if not using ACE auth
            if (!settings.UseAceAuthentication)
            {
                // Extract password from URL path (format: /password)
                // Path could be /rcon or /password if Rust-style auth
                string? password = null;
                string path = request.Path;

                if (path.Length > 1 && path != "/rcon")
                {
                    // Password is the path without leading slash
                    password = path.TrimStart('/');
                }

                ModManager.Log($"[RCON] WebSocket Rust-style auth: path='{path}', extracted_password='{password ?? "(null)"}', expected_password='{settings.RconPassword}'");

                // Validate password
                if (!string.IsNullOrEmpty(password) && password == settings.RconPassword)
                {
                    wsConnection.IsAuthenticated = true;
                    SetWebSocketAuthenticated(webSocket, true);
                    ModManager.Log($"[RCON] WebSocket authenticated via URL password");
                }
                else
                {
                    // Send error response and close
                    var errorResponse = new RconResponse
                    {
                        Identifier = 0,
                        Status = "error",
                        Message = "Invalid password"
                    };
                    ModManager.Log($"[RCON] WebSocket Rust-style auth FAILED: sending error and closing");
                    await SendResponseAsync(webSocket, errorResponse);
                    await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Invalid password", CancellationToken.None);
                    UnregisterWebSocket(webSocket);
                    return;
                }
            }

            // Handle WebSocket in background
            await HandleWebSocketConnectionAsync(webSocket, wsConnection, cancellationToken);
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR in WebSocket handler: {ex.Message}", ModManager.LogLevel.Error);
        }
    }

    /// <summary>
    /// Handle WebSocket connection lifecycle
    /// </summary>
    private async Task HandleWebSocketConnectionAsync(WebSocket webSocket, WebSocketRconConnection wsConnection, CancellationToken cancellationToken)
    {
        using (webSocket)
        {
            var buffer = new byte[1024 * 4];

            try
            {
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
                        var response = await RconProtocol.HandleCommandAsync(request, wsConnection, settings);

                        // Track authentication status for broadcast filtering
                        if (wsConnection.IsAuthenticated)
                        {
                            SetWebSocketAuthenticated(webSocket, true);
                        }

                        // Send response back through WebSocket
                        await SendResponseAsync(webSocket, response);
                    }
                    catch (WebSocketException wex) when (wex.Message.Contains("closed") || wex.Message.Contains("Aborted") || wex.Message.Contains("invalid state"))
                    {
                        // Normal shutdown
                        ModManager.Log($"[RCON] WebSocket closed normally", ModManager.LogLevel.Info);
                        break;
                    }
                    catch (WebSocketException wex)
                    {
                        ModManager.Log($"[RCON] WebSocket error: {wex.Message}", ModManager.LogLevel.Error);
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (!ex.Message.Contains("Aborted") && !ex.Message.Contains("invalid state") && !ex.Message.Contains("closed"))
                        {
                            ModManager.Log($"[RCON] ERROR in WebSocket handler: {ex.Message}", ModManager.LogLevel.Error);
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                ModManager.Log($"[RCON] ERROR handling WebSocket: {ex.Message}", ModManager.LogLevel.Error);
            }
            finally
            {
                // Unregister WebSocket from broadcasting
                UnregisterWebSocket(webSocket);
                ModManager.Log($"[RCON] WebSocket connection closed");
            }
        }
    }

    /// <summary>
    /// Send response via WebSocket
    /// </summary>
    private async Task SendResponseAsync(WebSocket webSocket, RconResponse response)
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

    /// <summary>
    /// Handle regular HTTP request for static files
    /// </summary>
    private async Task HandleHttpRequestAsync(NetworkStream stream, HttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var path = request.Path;

            // Default path to index.html
            if (path == "/" || string.IsNullOrEmpty(path))
                path = "/index.html";

            // Remove leading slash
            path = path.TrimStart('/');

            if (settings.EnableLogging)
                ModManager.Log($"[RCON] Static file request: {path}");

            // Get embedded resource
            var content = GetEmbeddedResource(path);
            if (content == null)
            {
                await SendHttpResponseAsync(stream, 404, "Not Found");
                return;
            }

            // Determine content type
            var contentType = GetContentType(path);

            // Send HTTP response
            var response = "HTTP/1.1 200 OK\r\n" +
                $"Content-Type: {contentType}\r\n" +
                $"Content-Length: {content.Length}\r\n" +
                "Cache-Control: no-cache\r\n" +
                "Access-Control-Allow-Origin: *\r\n" +
                "Connection: close\r\n" +
                "\r\n";

            await stream.WriteAsync(Encoding.UTF8.GetBytes(response), 0, response.Length);
            await stream.WriteAsync(content, 0, content.Length);
            await stream.FlushAsync();
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR handling HTTP request: {ex.Message}", ModManager.LogLevel.Error);
        }
    }

    /// <summary>
    /// Send HTTP response
    /// </summary>
    private async Task SendHttpResponseAsync(NetworkStream stream, int statusCode, string message)
    {
        try
        {
            var statusText = statusCode switch
            {
                400 => "Bad Request",
                404 => "Not Found",
                500 => "Internal Server Error",
                _ => "OK"
            };

            var response = $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                "Content-Type: text/plain\r\n" +
                $"Content-Length: {message.Length}\r\n" +
                "Connection: close\r\n" +
                "\r\n" +
                message;

            await stream.WriteAsync(Encoding.UTF8.GetBytes(response), 0, response.Length);
            await stream.FlushAsync();
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR sending HTTP response: {ex.Message}", ModManager.LogLevel.Error);
        }
    }

    /// <summary>
    /// Get embedded resource by path
    /// </summary>
    private byte[]? GetEmbeddedResource(string resourcePath)
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceName = $"RCON.webclient.public.{resourcePath.Replace('/', '.')}";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                return null;
            }

            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR loading embedded resource: {ex.Message}", ModManager.LogLevel.Error);
            return null;
        }
    }

    /// <summary>
    /// Get content type based on file extension
    /// </summary>
    private string GetContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".json" => "application/json",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            _ => "text/plain"
        };
    }

    /// <summary>
    /// Register a WebSocket connection for broadcasting
    /// </summary>
    public void RegisterWebSocket(WebSocket webSocket)
    {
        int wsId = Interlocked.Increment(ref webSocketIdCounter);
        var entry = new WebSocketEntry(webSocket);
        activeWebSockets.TryAdd(wsId, entry);

        if (settings.EnableLogging)
            ModManager.Log($"[RCON] WebSocket registered (ID: {wsId}, Total: {activeWebSockets.Count})", ModManager.LogLevel.Info);
    }

    /// <summary>
    /// Mark a WebSocket as authenticated
    /// </summary>
    public void SetWebSocketAuthenticated(WebSocket webSocket, bool authenticated = true)
    {
        var entry = activeWebSockets.Values.FirstOrDefault(e => e.WebSocket == webSocket);
        if (entry != null)
        {
            entry.IsAuthenticated = authenticated;
        }
    }

    /// <summary>
    /// Unregister a WebSocket connection
    /// </summary>
    public void UnregisterWebSocket(WebSocket webSocket)
    {
        var entry = activeWebSockets.FirstOrDefault(x => x.Value.WebSocket == webSocket);
        if (entry.Key != 0)
        {
            activeWebSockets.TryRemove(entry.Key, out _);

            if (settings.EnableLogging)
                ModManager.Log($"[RCON] WebSocket unregistered (ID: {entry.Key}, Remaining: {activeWebSockets.Count})", ModManager.LogLevel.Info);
        }
    }

    /// <summary>
    /// Broadcast a message to all AUTHENTICATED WebSocket connections only
    /// </summary>
    public void BroadcastMessage(RconResponse message)
    {
        var json = RconProtocol.FormatResponse(message);
        var data = Encoding.UTF8.GetBytes(json);

        foreach (var wsEntry in activeWebSockets)
        {
            try
            {
                var wsConnection = wsEntry.Value;
                var ws = wsConnection.WebSocket;

                // Only send to authenticated connections
                if (!wsConnection.IsAuthenticated)
                {
                    continue;
                }

                if (ws.State == WebSocketState.Open)
                {
                    ws.SendAsync(
                        new ArraySegment<byte>(data),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None).GetAwaiter().GetResult();
                }
                else
                {
                    // Clean up closed sockets
                    activeWebSockets.TryRemove(wsEntry.Key, out _);
                }
            }
            catch (Exception ex)
            {
                if (settings.EnableLogging)
                    ModManager.Log($"[RCON] ERROR broadcasting to WebSocket: {ex.Message}", ModManager.LogLevel.Warn);

                activeWebSockets.TryRemove(wsEntry.Key, out _);
            }
        }
    }
}

/// <summary>
/// Helper class for HTTP request parsing
/// </summary>
public class HttpRequest
{
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public string Version { get; set; } = "";
    public Dictionary<string, string> Headers { get; set; } = new();
    public bool IsWebSocketUpgrade { get; set; }
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
}
