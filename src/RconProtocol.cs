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

        // Reconstruct full command with args if args were provided
        if (!string.IsNullOrEmpty(command) && request.Args != null && request.Args.Count > 0)
        {
            command = command + " " + string.Join(" ", request.Args);
        }

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

        // Handle PLAYERS command - returns player list (allowed after auth)
        if (command == "players" && connection.IsAuthenticated)
        {
            return SetDebugFlag(HandlePlayers(request, settings), settings);
        }

        // Handle BANLIST command - returns list of banned accounts (allowed after auth)
        if (command == "banlist" && connection.IsAuthenticated)
        {
            return SetDebugFlag(await HandleBanlistAsync(request, settings), settings);
        }

        // Handle BANINFO command - returns detailed ban info for an account (allowed after auth)
        if (command.StartsWith("baninfo") && connection.IsAuthenticated)
        {
            return SetDebugFlag(HandleBaninfo(request, settings), settings);
        }

        // Handle BANREASON command - updates ban reason (allowed after auth)
        if (command.StartsWith("banreason") && connection.IsAuthenticated)
        {
            return SetDebugFlag(HandleBanreason(request, settings), settings);
        }

        // Handle UNBAN command - unbans an account (allowed after auth)
        if (command == "unban" && connection.IsAuthenticated)
        {
            return SetDebugFlag(HandleUnban(request, settings), settings);
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
    /// Handle authentication request
    /// Supports both Rust-style (password only) and ACE-style (username + password)
    /// </summary>
    private static async Task<RconResponse> HandleAuthAsync(RconRequest request, RconConnection connection, Settings? settings = null)
    {
        bool useAceAuth = settings?.UseAceAuthentication ?? false;

        if (useAceAuth)
        {
            // ACE-style: require both account name (in Name field) and password
            if (string.IsNullOrEmpty(request.Name) || string.IsNullOrEmpty(request.Password))
            {
                ModManager.Log($"[RCON] ACE auth attempt missing credentials - Name empty: {string.IsNullOrEmpty(request.Name)}, Password empty: {string.IsNullOrEmpty(request.Password)}");
                return new RconResponse
                {
                    Identifier = request.Identifier,
                    Status = "error",
                    Message = "Both account name and password required for ACE authentication"
                };
            }

            // Use RconAuthenticator to verify ACE account
            ModManager.Log($"[RCON] ACE auth attempt for account: {request.Name}");
            bool authenticated = await RconAuthenticator.AuthenticateAceAccountAsync(request.Name, request.Password);
            ModManager.Log($"[RCON] ACE auth result for {request.Name}: {(authenticated ? "SUCCESS" : "FAILED")}");

            if (!authenticated)
            {
                // Close connection on auth failure (don't allow further attempts)
                ModManager.Log($"[RCON] ACE auth failed - returning error response, will close connection");
                return new RconResponse
                {
                    Identifier = request.Identifier,
                    Status = "error",
                    Message = "Invalid account name or password, or account is not an admin"
                };
                // Note: Connection will be closed by RconHttpServer after sending this error response
                // due to WebSocketCloseStatus.PolicyViolation being set
            }

            // Mark connection as authenticated
            ModManager.Log($"[RCON] ACE auth succeeded for {request.Name}, marking connection as authenticated");
            connection.IsAuthenticated = true;

            // Auto-fetch server status to populate sidebar
            var statusData = GetServerStatus();

            return new RconResponse
            {
                Identifier = request.Identifier,
                Status = "authenticated",
                Message = "ACE authentication successful",
                Data = statusData
            };
        }
        else
        {
            // Rust-style: password only
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
            var maxPlayers = (int)ConfigManager.Config.Server.Network.MaximumAllowedSessions;

            return new Dictionary<string, object>
            {
                { "ServerName", ConfigManager.Config.Server.WorldName },
                { "Status", "Online" },
                { "CurrentPlayers", onlinePlayers },
                { "MaxPlayers", maxPlayers },
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
                        { "Location", player.Location?.ToLOCString() ?? "Unknown" },
                        { "AccountName", player.Session?.Account ?? "Unknown" }
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
        helloData["AceServerVersion"] = ServerBuildInfo.Version;
        helloData["AceServerBuild"] = ServerBuildInfo.Build;

        try
        {
            var dbVersion = DatabaseManager.World.GetVersion();
            helloData["AceDatabaseBaseVersion"] = dbVersion.BaseVersion;
            helloData["AceDatabasePatchVersion"] = dbVersion.PatchVersion;
        }
        catch
        {
            helloData["AceDatabaseBaseVersion"] = "Unknown";
            helloData["AceDatabasePatchVersion"] = "Unknown";
        }

        helloData["OnlinePlayers"] = GetOnlinePlayersList();

        var response = new RconResponse
        {
            Identifier = request.Identifier,
            Status = "success",
            Message = "Server hello",
            Data = helloData
        };

        if (settings?.DebugMode ?? false)
        {
            var json = JsonSerializer.Serialize(response);
            ModManager.Log($"[RCON] HELLO response: {json}");
        }

        return response;
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

            // Add version info
            statusData["AceServerVersion"] = ServerBuildInfo.Version;
            statusData["AceServerBuild"] = ServerBuildInfo.Build;

            try
            {
                var dbVersion = DatabaseManager.World.GetVersion();
                statusData["AceDatabaseBaseVersion"] = dbVersion.BaseVersion;
                statusData["AceDatabasePatchVersion"] = dbVersion.PatchVersion;
            }
            catch
            {
                statusData["AceDatabaseBaseVersion"] = "Unknown";
                statusData["AceDatabasePatchVersion"] = "Unknown";
            }

            var response = new RconResponse
            {
                Identifier = request.Identifier,
                Status = "success",
                Message = "Server status",
                Data = statusData
            };

            if (settings?.DebugMode ?? false)
            {
                var json = JsonSerializer.Serialize(response);
                ModManager.Log($"[RCON] STATUS response: {json}");
            }

            return response;
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

    /// <summary>
    /// Handle PLAYERS command - returns current player count and list
    /// </summary>
    private static RconResponse HandlePlayers(RconRequest request, Settings? settings)
    {
        var playersData = new Dictionary<string, object>();

        try
        {
            var onlinePlayers = PlayerManager.GetAllOnline()?.Count ?? 0;

            playersData["CurrentPlayers"] = onlinePlayers;
            playersData["MaxPlayers"] = (int)ConfigManager.Config.Server.Network.MaximumAllowedSessions;
            playersData["OnlinePlayers"] = GetOnlinePlayersList();

            var response = new RconResponse
            {
                Identifier = request.Identifier,
                Status = "success",
                Message = "Player list",
                Data = playersData
            };

            if (settings?.DebugMode ?? false)
            {
                var json = JsonSerializer.Serialize(response);
                ModManager.Log($"[RCON] PLAYERS response: {json}");
            }

            return response;
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR in HandlePlayers: {ex.Message}", ModManager.LogLevel.Error);
            return new RconResponse
            {
                Identifier = request.Identifier,
                Status = "error",
                Message = $"Error getting player list: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Handle banlist command - directly build the ban list from ACE's data
    /// Since the banlist command uses logging instead of Console.Out, we need a different approach
    /// </summary>
    private static async Task<RconResponse> HandleBanlistAsync(RconRequest request, Settings? settings)
    {
        try
        {
            var bannedAccounts = new List<Dictionary<string, object>>();

            // Execute the actual banlist command to trigger ACE's ban checking logic
            // Even though we can't capture its output directly, this ensures bans are current
            string command = "";
            string[] parameters = System.Array.Empty<string>();
            CommandManager.ParseCommand("banlist", out command, out parameters);
            var handlerResponse = CommandManager.GetCommandHandler(null, command, parameters, out var commandInfo);

            if (handlerResponse == CommandHandlerResponse.Ok && commandInfo.Handler is CommandHandler handler)
            {
                try
                {
                    // Just invoke it to ensure ACE's internal ban list is updated
                    handler.Invoke(null, parameters);
                }
                catch { }
            }

            // Now get the bans from the database directly
            // Query all accounts and find those with active bans
            bannedAccounts = GetActiveBannedAccountsFromAPI();

            var bansData = new Dictionary<string, object>
            {
                { "BannedAccounts", bannedAccounts },
                { "Count", bannedAccounts.Count }
            };

            var response = new RconResponse
            {
                Identifier = request.Identifier,
                Status = "success",
                Message = "Banned accounts list",
                Data = bansData
            };

            if (settings?.DebugMode ?? false)
            {
                var json = JsonSerializer.Serialize(response);
                ModManager.Log($"[RCON] BANLIST response: {json}");
            }

            return response;
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR in HandleBanlist: {ex.Message}", ModManager.LogLevel.Error);
            return new RconResponse
            {
                Identifier = request.Identifier,
                Status = "error",
                Message = $"Error getting banned accounts list: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Handle baninfo command - get detailed ban info for a specific account
    /// </summary>
    private static RconResponse HandleBaninfo(RconRequest request, Settings? settings)
    {
        try
        {
            if (request.Args == null || request.Args.Count == 0)
            {
                return new RconResponse
                {
                    Identifier = request.Identifier,
                    Status = "error",
                    Message = "Account name required. Usage: baninfo <accountname>"
                };
            }

            string accountName = request.Args[0];

            // Query database directly for ban info
            var authDb = DatabaseManager.Authentication;
            if (authDb == null)
            {
                return new RconResponse
                {
                    Identifier = request.Identifier,
                    Status = "error",
                    Message = "Database not available"
                };
            }

            // Try to find the account by name
            var account = authDb.GetAccountByName(accountName);
            if (account == null)
            {
                return new RconResponse
                {
                    Identifier = request.Identifier,
                    Status = "error",
                    Message = $"Account '{accountName}' not found"
                };
            }

            // Check if account has an active ban
            var now = DateTime.UtcNow;
            if (!account.BanExpireTime.HasValue || account.BanExpireTime <= now)
            {
                return new RconResponse
                {
                    Identifier = request.Identifier,
                    Status = "error",
                    Message = $"Account '{accountName}' is not banned"
                };
            }

            // Build response with ban info
            var banInfo = new Dictionary<string, object>
            {
                { "AccountName", account.AccountName ?? "Unknown" },
                { "BanExpireTime", account.BanExpireTime.Value.ToString("MMM dd yyyy h:mmtt") },
                { "BanReason", account.BanReason ?? "No reason specified" },
                { "Characters", new List<Dictionary<string, object>>() } // Empty for now, could be populated from character DB
            };

            var response = new RconResponse
            {
                Identifier = request.Identifier,
                Status = "success",
                Message = "Ban details",
                Data = banInfo
            };

            if (settings?.DebugMode ?? false)
            {
                var json = JsonSerializer.Serialize(response);
                ModManager.Log($"[RCON] BANINFO response: {json}");
            }

            return response;
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR in HandleBaninfo: {ex.Message}", ModManager.LogLevel.Error);
            return new RconResponse
            {
                Identifier = request.Identifier,
                Status = "error",
                Message = $"Error getting ban details: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Handle banreason command - web client sends this to update ban reason display
    /// Note: ACE doesn't have a native command to change ban reason, so this just acknowledges the request
    /// </summary>
    private static RconResponse HandleBanreason(RconRequest request, Settings? settings)
    {
        try
        {
            if (request.Args == null || request.Args.Count < 2)
            {
                return new RconResponse
                {
                    Identifier = request.Identifier,
                    Status = "error",
                    Message = "Usage: banreason <accountname> <reason>"
                };
            }

            string accountName = request.Args[0];
            string newReason = string.Join(" ", request.Args.Skip(1));

            ModManager.Log($"[RCON] Ban reason update requested for account '{accountName}': {newReason}", ModManager.LogLevel.Info);

            return new RconResponse
            {
                Identifier = request.Identifier,
                Status = "success",
                Message = $"Ban reason noted (ACE does not support direct reason updates via RCON)"
            };
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR in HandleBanreason: {ex.Message}", ModManager.LogLevel.Error);
            return new RconResponse
            {
                Identifier = request.Identifier,
                Status = "error",
                Message = $"Error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Handle unban command - unbans an account
    /// </summary>
    private static RconResponse HandleUnban(RconRequest request, Settings? settings)
    {
        try
        {
            if (request.Args == null || request.Args.Count == 0)
            {
                return new RconResponse
                {
                    Identifier = request.Identifier,
                    Status = "error",
                    Message = "Account name required. Usage: unban <accountname>"
                };
            }

            string accountName = request.Args[0];

            // Query database to find the account
            var authDb = DatabaseManager.Authentication;
            if (authDb == null)
            {
                return new RconResponse
                {
                    Identifier = request.Identifier,
                    Status = "error",
                    Message = "Database not available"
                };
            }

            var account = authDb.GetAccountByName(accountName);
            if (account == null)
            {
                return new RconResponse
                {
                    Identifier = request.Identifier,
                    Status = "error",
                    Message = $"Account '{accountName}' not found"
                };
            }

            // Check if account has an active ban
            var now = DateTime.UtcNow;
            if (!account.BanExpireTime.HasValue || account.BanExpireTime <= now)
            {
                return new RconResponse
                {
                    Identifier = request.Identifier,
                    Status = "error",
                    Message = $"Account '{accountName}' is not banned"
                };
            }

            // Clear the ban by setting BanExpireTime to null
            account.BanExpireTime = null;
            account.BanReason = null;

            // Save the changes to the database
            authDb.UpdateAccount(account);

            ModManager.Log($"[RCON] Account '{accountName}' has been unbanned", ModManager.LogLevel.Info);

            return new RconResponse
            {
                Identifier = request.Identifier,
                Status = "success",
                Message = $"Account '{accountName}' has been unbanned"
            };
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR in HandleUnban: {ex.Message}", ModManager.LogLevel.Error);
            return new RconResponse
            {
                Identifier = request.Identifier,
                Status = "error",
                Message = $"Error unbanning account: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Get active banned accounts by querying ACE's authentication database
    /// Loops through all accounts and filters for those with active bans
    /// </summary>
    private static List<Dictionary<string, object>> GetActiveBannedAccountsFromAPI()
    {
        var bannedAccounts = new List<Dictionary<string, object>>();

        try
        {
            var authDb = DatabaseManager.Authentication;
            if (authDb == null)
            {
                ModManager.Log($"[RCON] DatabaseManager.Authentication is null", ModManager.LogLevel.Warn);
                return bannedAccounts;
            }

            var now = DateTime.UtcNow;
            int totalAccounts = authDb.GetAccountCount();
            int bannedCount = 0;

            ModManager.Log($"[RCON] Scanning {totalAccounts} accounts for active bans", ModManager.LogLevel.Info);

            // We need to iterate through account IDs to find banned ones
            // Since ACE doesn't provide a bulk query, we'll scan key account IDs
            // The banlist command in ACE likely does this internally

            // Try a more pragmatic approach: scan reasonable account ID range
            // Most servers won't have millions of accounts
            for (uint accountId = 1; accountId <= Math.Min(totalAccounts + 100, 10000); accountId++)
            {
                try
                {
                    var account = authDb.GetAccountById(accountId);
                    if (account != null && account.BanExpireTime.HasValue && account.BanExpireTime > now)
                    {
                        var banInfo = new Dictionary<string, object>
                        {
                            { "AccountName", account.AccountName ?? "Unknown" },
                            { "BanExpireTime", account.BanExpireTime.Value.ToString("MMM dd yyyy h:mmtt") },
                            { "BanReason", account.BanReason ?? "No reason specified" }
                        };
                        bannedAccounts.Add(banInfo);
                        bannedCount++;
                    }
                }
                catch { }
            }

            ModManager.Log($"[RCON] Found {bannedCount} active banned accounts", ModManager.LogLevel.Info);
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] Error in GetActiveBannedAccountsFromAPI: {ex.Message}", ModManager.LogLevel.Error);
        }

        return bannedAccounts;
    }

    /// <summary>
    /// Parse ACE's banlist command output into structured data
    /// Format: accountname -- banned by account root until server time [MonthName] [Day] [Year] [Time] -- Reason: [reason]
    /// </summary>
    private static List<Dictionary<string, object>> ParseBanlistOutput(string output)
    {
        var bannedAccounts = new List<Dictionary<string, object>>();

        if (string.IsNullOrEmpty(output))
        {
            ModManager.Log($"[RCON] ParseBanlistOutput: output is empty or null", ModManager.LogLevel.Debug);
            return bannedAccounts;
        }

        ModManager.Log($"[RCON] ParseBanlistOutput: processing {output.Length} characters", ModManager.LogLevel.Debug);

        var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        ModManager.Log($"[RCON] ParseBanlistOutput: found {lines.Length} lines", ModManager.LogLevel.Debug);

        foreach (var line in lines)
        {
            // Skip header and footer lines
            if (string.IsNullOrWhiteSpace(line) || line.Contains("---") || line.Contains("following accounts") || line.Contains("INFO") || line.Contains(":"))
                continue;

            ModManager.Log($"[RCON] ParseBanlistOutput: processing line: {line.Substring(0, Math.Min(100, line.Length))}", ModManager.LogLevel.Debug);

            // Parse format: accountname -- banned by account root until server time [MonthName] [Day] [Year] [Time] -- Reason: [reason]
            // Split by " -- " separator to extract parts
            var parts = line.Split(new[] { " -- " }, StringSplitOptions.None);

            if (parts.Length >= 3)
            {
                try
                {
                    var accountName = parts[0].Trim();
                    var middlePart = parts[1];  // "banned by account root until server time [date time]"
                    var reasonPart = parts[2];  // "Reason: [reason text]"

                    // Extract the date/time from middle part
                    // Format: "banned by account root until server time Mar 29 2051  8:13PM"
                    var timeMatch = System.Text.RegularExpressions.Regex.Match(
                        middlePart,
                        @"until server time (.+)$"
                    );

                    string expireTimeStr = "";
                    if (timeMatch.Success)
                    {
                        expireTimeStr = timeMatch.Groups[1].Value.Trim();
                    }

                    // Extract reason from "Reason: [text]"
                    var reasonMatch = System.Text.RegularExpressions.Regex.Match(
                        reasonPart,
                        @"^Reason:\s*(.*)$"
                    );

                    string reason = "";
                    if (reasonMatch.Success)
                    {
                        reason = reasonMatch.Groups[1].Value.Trim();
                    }

                    if (!string.IsNullOrEmpty(accountName))
                    {
                        var banInfo = new Dictionary<string, object>
                        {
                            { "AccountName", accountName },
                            { "BanExpireTime", expireTimeStr },
                            { "BanReason", reason }
                        };

                        bannedAccounts.Add(banInfo);
                        ModManager.Log($"[RCON] Parsed ban: {accountName} expires {expireTimeStr}", ModManager.LogLevel.Debug);
                    }
                }
                catch (Exception ex)
                {
                    ModManager.Log($"[RCON] Error parsing ban line: {line} - {ex.Message}", ModManager.LogLevel.Debug);
                }
            }
        }

        ModManager.Log($"[RCON] ParseBanlistOutput: parsed {bannedAccounts.Count} banned accounts", ModManager.LogLevel.Debug);
        return bannedAccounts;
    }
}
