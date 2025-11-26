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
    /// Handle incoming RCON command
    /// </summary>
    public static async Task<RconResponse> HandleCommandAsync(RconRequest request, RconConnection connection)
    {
        ModManager.Log($"[RCON] Command: {request.Command}, IsAuthenticated: {connection.IsAuthenticated}");

        // If not authenticated, require auth command
        if (!connection.IsAuthenticated)
        {
            if (request.Command != "auth")
            {
                return new RconResponse
                {
                    Identifier = request.Identifier,
                    Status = "error",
                    Message = "Authentication required. Send 'auth' command with password first."
                };
            }

            // Handle authentication
            var response = await HandleAuthAsync(request, connection);
            ModManager.Log($"[RCON] After auth attempt - IsAuthenticated: {connection.IsAuthenticated}");
            return response;
        }

        // Handle authenticated commands
        return request.Command switch
        {
            "status" => HandleStatus(request),
            "players" => HandlePlayers(request),
            "landblocks" => HandleLandblocks(request),
            "help" => HandleHelp(request),
            _ => new RconResponse
            {
                Identifier = request.Identifier,
                Status = "error",
                Message = $"Unknown command: {request.Command}. Type 'help' for available commands."
            }
        };
    }

    /// <summary>
    /// Handle authentication request
    /// </summary>
    private static async Task<RconResponse> HandleAuthAsync(RconRequest request, RconConnection connection)
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

        return new RconResponse
        {
            Identifier = request.Identifier,
            Status = "authenticated",
            Message = "Authentication successful"
        };
    }

    /// <summary>
    /// Handle status command
    /// </summary>
    private static RconResponse HandleStatus(RconRequest request)
    {
        try
        {
            var statusData = GetServerStatus();
            ModManager.Log($"[RCON] Status data: Players={statusData["CurrentPlayers"]}, Uptime={statusData["Uptime"]}, Status={statusData["Status"]}", ModManager.LogLevel.Info);

            return new RconResponse
            {
                Identifier = request.Identifier,
                Status = "success",
                Message = "Server status retrieved",
                Data = statusData
            };
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] Status error: {ex.Message}", ModManager.LogLevel.Error);
            return new RconResponse
            {
                Identifier = request.Identifier,
                Status = "error",
                Message = $"Error getting status: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Handle players command - get online player list
    /// </summary>
    private static RconResponse HandlePlayers(RconRequest request)
    {
        try
        {
            var players = GetOnlinePlayersList();
            ModManager.Log($"[RCON] Players data: count={players.Count}", ModManager.LogLevel.Info);

            return new RconResponse
            {
                Identifier = request.Identifier,
                Status = "success",
                Message = "Player list retrieved",
                Data = new Dictionary<string, object>
                {
                    { "players", players },
                    { "count", players.Count }
                }
            };
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] Players error: {ex.Message}", ModManager.LogLevel.Error);
            return new RconResponse
            {
                Identifier = request.Identifier,
                Status = "error",
                Message = $"Error getting players: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Handle landblocks command - get loaded landblock info
    /// </summary>
    private static RconResponse HandleLandblocks(RconRequest request)
    {
        try
        {
            var landblocks = GetLandblockInfo();
            ModManager.Log($"[RCON] Landblocks data: count={landblocks.Count}", ModManager.LogLevel.Info);

            return new RconResponse
            {
                Identifier = request.Identifier,
                Status = "success",
                Message = "Landblock info retrieved",
                Data = new Dictionary<string, object>
                {
                    { "landblocks", landblocks },
                    { "count", landblocks.Count }
                }
            };
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] Landblocks error: {ex.Message}", ModManager.LogLevel.Error);
            return new RconResponse
            {
                Identifier = request.Identifier,
                Status = "error",
                Message = $"Error getting landblocks: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Handle help command
    /// </summary>
    private static RconResponse HandleHelp(RconRequest request)
    {
        var helpText = @"Available RCON Commands:
  status    - Get server status information
  players   - Get list of online players
  landblocks - Get loaded landblock information
  help      - Show this help message

Use: {""Command"": ""command_name"", ""Identifier"": 1}
Auth: {""Command"": ""auth"", ""Password"": ""your_password"", ""Identifier"": 1}";

        return new RconResponse
        {
            Identifier = request.Identifier,
            Status = "success",
            Message = helpText
        };
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
                { "ServerName", "Asheron's Call" },
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
            // Get process uptime
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var uptime = DateTime.UtcNow - process.StartTime;

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
}
