namespace RCON;

/// <summary>
/// RCON Protocol Handler
/// Parses JSON messages, routes commands, formats responses
/// </summary>
public static class RconProtocol
{
    /// <summary>
    /// Parse incoming JSON message
    /// </summary>
    public static RconRequest? ParseMessage(string json)
    {
        try
        {
            var request = JsonSerializer.Deserialize<RconRequest>(json);
            return request;
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR parsing JSON: {ex.Message}", ModManager.LogLevel.Warn);
            return null;
        }
    }

    /// <summary>
    /// Format response to JSON
    /// </summary>
    public static string FormatResponse(RconResponse response)
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        return JsonSerializer.Serialize(response, options);
    }

    /// <summary>
    /// Set Debug flag on response based on settings
    /// </summary>
    private static RconResponse SetDebugFlag(RconResponse response, Settings? settings)
    {
        if (settings != null)
        {
            response.Debug = settings.DebugMode;
        }
        return response;
    }

    /// <summary>
    /// Handle incoming RCON command - Rust-style passthrough
    /// Supports both Rust-style URL auth and ACE packet-based auth
    /// </summary>
    public static async Task<RconResponse> HandleCommandAsync(RconRequest request, RconConnection connection, Settings? settings = null)
    {
        bool useAceAuth = settings?.UseAceAuthentication ?? false;
        string command = request.Command ?? request.Message ?? "";

        if (settings?.EnableLogging ?? false)
            ModManager.Log($"[RCON] Command: {command}, IsAuthenticated: {connection.IsAuthenticated}, UseAceAuth: {useAceAuth}");

        // Allow config request without authentication (for web client to detect auth mode)
        if (command == "config")
        {
            return SetDebugFlag(HandleConfig(request, settings), settings);
        }

        // Handle HELLO command - returns initial server state (allowed after auth)
        if (command == "hello" && connection.IsAuthenticated)
        {
            return SetDebugFlag(HandleHello(request, settings), settings);
        }

        // Handle STATUS command - returns detailed server status (allowed after auth)
        if (command == "status" && connection.IsAuthenticated)
        {
            return SetDebugFlag(HandleStatus(request, settings), settings);
        }

        // Handle authentication if using ACE auth mode
        if (useAceAuth && !connection.IsAuthenticated)
        {
            // In ACE auth mode, only auth command is allowed before authentication
            if (command != "auth")
            {
                var errorResponse = new RconResponse
                {
                    Identifier = request.Identifier,
                    Status = "error",
                    Message = "Authentication required. Send 'auth' command with password first."
                };
                return SetDebugFlag(errorResponse, settings);
            }

            // Handle authentication
            var authResponse = await HandleAuthAsync(request, connection, settings);
            return SetDebugFlag(authResponse, settings);
        }

        // If using Rust-style URL auth and not authenticated, reject
        if (!useAceAuth && !connection.IsAuthenticated)
        {
            var errorResponse = new RconResponse
            {
                Identifier = request.Identifier,
                Status = "error",
                Message = "Authentication required. Connection must include password in URL."
            };
            return SetDebugFlag(errorResponse, settings);
        }

        // Connection is authenticated - execute the command via ACE CommandManager (passthrough style)
        var response = await ExecuteAceConsoleCommandAsync(request, command, settings);

        return SetDebugFlag(response, settings);
    }

    /// <summary>
    /// Handle authentication request (ACE auth mode)
    /// </summary>
    private static async Task<RconResponse> HandleAuthAsync(RconRequest request, RconConnection connection, Settings? settings = null)
    {
        if (string.IsNullOrEmpty(request.Password))
        {
            return new RconResponse
            {
                Identifier = request.Identifier,
                Status = "error",
                Message = "Password required for authentication"
            };
        }

        // Use RconAuthenticator to verify
        bool authenticated = await RconAuthenticator.AuthenticateAsync(request.Password);

        if (!authenticated)
        {
            return new RconResponse
            {
                Identifier = request.Identifier,
                Status = "error",
                Message = "Invalid password"
            };
        }

        // Mark connection as authenticated
        connection.IsAuthenticated = true;

        // Auto-fetch server status to populate sidebar
        var statusData = GetServerStatus();

        return new RconResponse
        {
            Identifier = request.Identifier,
            Status = "authenticated",
            Message = "Authentication successful",
            Data = statusData
        };
    }

    /// <summary>
    /// Execute any ACE console command via CommandManager (passthrough style)
    /// This replaces all hardcoded command handlers
    /// </summary>
    private static Task<RconResponse> ExecuteAceConsoleCommandAsync(RconRequest request, string commandText, Settings? settings)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(commandText))
            {
                return Task.FromResult(new RconResponse
                {
                    Identifier = request.Identifier,
                    Status = "error",
                    Message = "No command specified"
                });
            }

            if (settings?.EnableLogging ?? false)
                ModManager.Log($"[RCON] Executing command: {commandText}", ModManager.LogLevel.Info);

            // Capture console output
            var outputCapture = new System.IO.StringWriter();
            var originalOut = System.Console.Out;

            try
            {
                // Redirect Console.Out to capture output
                System.Console.SetOut(outputCapture);

                // Parse the command using ACE's CommandManager
                string command = "";
                string[] parameters = System.Array.Empty<string>();
                CommandManager.ParseCommand(commandText, out command, out parameters);

                // Get the command handler
                var handlerResponse = CommandManager.GetCommandHandler(null, command, parameters, out var commandInfo);

                if (handlerResponse == CommandHandlerResponse.Ok)
                {
                    // Invoke the command handler
                    ((CommandHandler)commandInfo.Handler).Invoke(null, parameters);

                    // Get captured output
                    string output = outputCapture.ToString().Trim();

                    if (settings?.EnableLogging ?? false)
                        ModManager.Log($"[RCON] Command executed successfully, output length: {output.Length}", ModManager.LogLevel.Info);

                    return Task.FromResult(new RconResponse
                    {
                        Identifier = request.Identifier,
                        Status = "success",
                        Message = output,
                        Type = "Generic"
                    });
                }
                else
                {
                    // Command handler not found or validation failed
                    string errorMsg = $"Command failed: {handlerResponse}";

                    if (settings?.EnableLogging ?? false)
                        ModManager.Log($"[RCON] Command failed: {errorMsg}", ModManager.LogLevel.Warn);

                    return Task.FromResult(new RconResponse
                    {
                        Identifier = request.Identifier,
                        Status = "error",
                        Message = errorMsg,
                        Type = "Error"
                    });
                }
            }
            catch (Exception ex)
            {
                ModManager.Log($"[RCON] ERROR executing command: {ex.Message}", ModManager.LogLevel.Error);
                return Task.FromResult(new RconResponse
                {
                    Identifier = request.Identifier,
                    Status = "error",
                    Message = $"Error executing command: {ex.Message}",
                    Type = "Error"
                });
            }
            finally
            {
                // Restore original Console output
                System.Console.SetOut(originalOut);
            }
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] Unexpected error: {ex.Message}", ModManager.LogLevel.Error);
            return Task.FromResult(new RconResponse
            {
                Identifier = request.Identifier,
                Status = "error",
                Message = $"Internal server error: {ex.Message}",
                Type = "Error"
            });
        }
    }

    /// <summary>
    /// Get server status information
    /// </summary>
    private static Dictionary<string, object> GetServerStatus()
    {
        try
        {
            var onlinePlayers = PlayerManager.GetAllOnline()?.Count ?? 0;
            var uptime = GetServerUptime();

            return new Dictionary<string, object>
            {
                { "ServerName", ConfigManager.Config.Server.WorldName },
                { "Status", "Online" },
                { "CurrentPlayers", onlinePlayers },
                { "MaxPlayers", 255 },
                { "Uptime", uptime },
                { "WorldTime", DateTime.UtcNow.ToString("O") }
            };
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR getting status: {ex.Message}", ModManager.LogLevel.Error);
            throw;
        }
    }

    /// <summary>
    /// Get list of online players
    /// </summary>
    private static List<Dictionary<string, object>> GetOnlinePlayersList()
    {
        var playerList = new List<Dictionary<string, object>>();

        try
        {
            var players = PlayerManager.GetAllOnline();

            foreach (var player in players)
            {
                try
                {
                    playerList.Add(new Dictionary<string, object>
                    {
                        { "Name", player.Name ?? "Unknown" },
                        { "Guid", player.Guid.Full },
                        { "Level", player.Level ?? 1 },
                        { "Race", player.HeritageGroup.ToString() },
                        { "Location", player.Location?.ToLOCString() ?? "Unknown" }
                    });
                }
                catch (Exception ex)
                {
                    ModManager.Log($"[RCON] ERROR processing player {player.Name}: {ex.Message}", ModManager.LogLevel.Warn);
                }
            }
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR getting player list: {ex.Message}", ModManager.LogLevel.Error);
            throw;
        }

        return playerList;
    }

    /// <summary>
    /// Get loaded landblock information
    /// </summary>
    private static List<Dictionary<string, object>> GetLandblockInfo()
    {
        var landblockList = new List<Dictionary<string, object>>();

        try
        {
            var landblocks = LandblockManager.GetLoadedLandblocks();

            foreach (var landblock in landblocks)
            {
                try
                {
                    var id = "0x" + landblock.Id.ToString()[0..4];
                    var status = landblock.Permaload ? "Permaload" :
                                landblock.IsDormant ? "Dormant" : "Active";

                    landblockList.Add(new Dictionary<string, object>
                    {
                        { "Id", id },
                        { "Status", status },
                        { "X", landblock.Id.LandblockX.ToString("x2") },
                        { "Y", landblock.Id.LandblockY.ToString("x2") },
                        { "IsDungeon", landblock.IsDungeon },
                        { "HasDungeon", landblock.HasDungeon },
                        { "Players", landblock.GetPlayers().Count },
                        { "Creatures", landblock.GetCreatures().Count }
                    });
                }
                catch (Exception ex)
                {
                    ModManager.Log($"[RCON] ERROR processing landblock {landblock.Id}: {ex.Message}", ModManager.LogLevel.Warn);
                }
            }
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR getting landblock list: {ex.Message}", ModManager.LogLevel.Error);
            throw;
        }

        return landblockList;
    }

    /// <summary>
    /// Get formatted server uptime
    /// </summary>
    private static string GetServerUptime()
    {
        try
        {
            // Get process uptime - StartTime is local, convert to UTC
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var startTimeUtc = process.StartTime.Kind == DateTimeKind.Local
                ? process.StartTime.ToUniversalTime()
                : process.StartTime;
            var uptime = DateTime.UtcNow - startTimeUtc;

            var days = uptime.Days;
            var hours = uptime.Hours;
            var minutes = uptime.Minutes;
            var seconds = uptime.Seconds;

            return $"{days}d {hours}h {minutes}m {seconds}s";
        }
        catch
        {
            return "Unknown";
        }
    }

    /// <summary>
    /// Handle config request - returns client configuration settings
    /// </summary>
    private static RconResponse HandleConfig(RconRequest request, Settings? settings)
    {
        var configData = new Dictionary<string, object>
        {
            { "Version", RconConnection.ModVersion },
            { "RconEnabled", settings?.RconEnabled ?? true },
            { "WebRconEnabled", settings?.WebRconEnabled ?? true },
            { "MaxConnections", settings?.MaxConnections ?? 10 },
            { "ConnectionTimeoutSeconds", settings?.ConnectionTimeoutSeconds ?? 300 },
            { "EnableLogging", settings?.EnableLogging ?? false },
            { "DebugMode", settings?.DebugMode ?? false },
            { "AutoRefreshPlayers", settings?.AutoRefreshPlayers ?? true },
            { "MaxReconnectAttempts", settings?.MaxReconnectAttempts ?? 42 },
            { "ReconnectDelayMs", settings?.ReconnectDelayMs ?? 15000 },
            { "UseAceAuthentication", settings?.UseAceAuthentication ?? false }
        };

        return new RconResponse
        {
            Identifier = request.Identifier,
            Status = "success",
            Message = "Client configuration",
            Data = configData
        };
    }

    /// <summary>
    /// Handle HELLO command - returns initial server state needed by clients
    /// Sent automatically to clients after authentication
    /// Includes player list for immediate display
    /// </summary>
    private static RconResponse HandleHello(RconRequest request, Settings? settings)
    {
        var helloData = GetServerStatus();
        helloData["Version"] = RconConnection.ModVersion;
        helloData["OnlinePlayers"] = GetOnlinePlayersList();

        return new RconResponse
        {
            Identifier = request.Identifier,
            Status = "success",
            Message = "Server hello",
            Data = helloData
        };
    }

    /// <summary>
    /// Handle STATUS command - returns detailed server monitoring data
    /// Lean response for periodic polling (no ServerName since it doesn't change)
    /// </summary>
    private static RconResponse HandleStatus(RconRequest request, Settings? settings)
    {
        var statusData = new Dictionary<string, object>();

        try
        {
            // Add basic status info (excluding ServerName which doesn't change)
            var basicStatus = GetServerStatus();
            foreach (var kvp in basicStatus)
            {
                if (kvp.Key != "ServerName")  // Skip ServerName for STATUS
                {
                    statusData[kvp.Key] = kvp.Value;
                }
            }

            // Add version
            statusData["Version"] = RconConnection.ModVersion;

            // Add online players
            statusData["OnlinePlayers"] = GetOnlinePlayersList();

            return new RconResponse
            {
                Identifier = request.Identifier,
                Status = "success",
                Message = "Server status",
                Data = statusData
            };
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR in HandleStatus: {ex.Message}", ModManager.LogLevel.Error);
            return new RconResponse
            {
                Identifier = request.Identifier,
                Status = "error",
                Message = $"Error getting status: {ex.Message}"
            };
        }
    }
}
