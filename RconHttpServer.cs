namespace RCON;

/// <summary>
/// HTTP Server for Web Client
/// Serves embedded HTML/CSS/JS files and handles WebSocket connections
/// </summary>
public class RconHttpServer
{
    private readonly Settings settings;
    private HttpListener? httpListener;
    private CancellationTokenSource? cancellationTokenSource;
    private Task? acceptTask;

    public RconHttpServer(Settings settings)
    {
        this.settings = settings;
    }

    /// <summary>
    /// Start the HTTP server
    /// </summary>
    public void Start()
    {
        try
        {
            cancellationTokenSource = new CancellationTokenSource();
            httpListener = new HttpListener();
            httpListener.Prefixes.Add($"http://127.0.0.1:2948/");
            httpListener.Start();

            ModManager.Log($"[RCON] Web server started on http://127.0.0.1:2948/");

            // Start accepting requests
            acceptTask = AcceptRequestsAsync(cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR starting web server: {ex.Message}", ModManager.LogLevel.Error);
            throw;
        }
    }

    /// <summary>
    /// Stop the HTTP server
    /// </summary>
    public void Stop()
    {
        try
        {
            cancellationTokenSource?.Cancel();
            httpListener?.Stop();
            httpListener?.Close();
            httpListener = null;

            if (acceptTask != null && !acceptTask.IsCompleted)
            {
                acceptTask.Wait(TimeSpan.FromSeconds(5));
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
    /// Accept and handle HTTP requests
    /// </summary>
    private async Task AcceptRequestsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && httpListener != null)
        {
            try
            {
                var context = await httpListener.GetContextAsync();

                // Handle request asynchronously
                _ = HandleRequestAsync(context, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                // Server shutting down
                break;
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    ModManager.Log($"[RCON] ERROR accepting request: {ex.Message}", ModManager.LogLevel.Error);
                }
            }
        }
    }

    /// <summary>
    /// Handle individual HTTP request
    /// </summary>
    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;

            // Check if WebSocket upgrade request
            if (request.IsWebSocketRequest)
            {
                if (settings.EnableLogging)
                    ModManager.Log($"[RCON] WebSocket upgrade request: {request.Url}");

                try
                {
                    var wsContext = await context.AcceptWebSocketAsync(null);
                    var webSocket = wsContext.WebSocket;

                    // Handle WebSocket connection
                    await RconWebSocketHandler.HandleWebSocketAsync(webSocket, settings);
                }
                catch (Exception ex)
                {
                    ModManager.Log($"[RCON] ERROR handling WebSocket: {ex.Message}", ModManager.LogLevel.Error);
                    response.StatusCode = 500;
                    response.Close();
                }
            }
            else
            {
                // Handle static file request
                await HandleStaticFileAsync(context, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR handling request: {ex.Message}", ModManager.LogLevel.Error);
            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch { }
        }
    }

    /// <summary>
    /// Serve static files from embedded resources
    /// </summary>
    private async Task HandleStaticFileAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;

        // Get requested path
        var path = request.Url?.LocalPath ?? "/";
        if (path == "/")
            path = "/index.html";

        // Remove leading slash
        path = path.TrimStart('/');

        if (settings.EnableLogging)
            ModManager.Log($"[RCON] Static file request: {path}");

        try
        {
            // Get embedded resource
            var content = GetEmbeddedResource(path);

            if (content == null)
            {
                response.StatusCode = 404;
                var notFound = Encoding.UTF8.GetBytes("File not found");
                await response.OutputStream.WriteAsync(notFound, 0, notFound.Length, cancellationToken);
                response.Close();
                return;
            }

            // Set content type
            var contentType = GetContentType(path);
            response.ContentType = contentType;
            response.StatusCode = 200;

            // Write response
            await response.OutputStream.WriteAsync(content, 0, content.Length, cancellationToken);
            response.Close();
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR serving file {path}: {ex.Message}", ModManager.LogLevel.Error);
            response.StatusCode = 500;
            response.Close();
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
}
