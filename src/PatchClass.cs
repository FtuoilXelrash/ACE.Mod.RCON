
namespace RCON;

[HarmonyPatch]
public class PatchClass(BasicMod mod, string settingsName = "Settings.json") : BasicPatch<Settings>(mod, settingsName)
{
    private static RconServer? rconServer;
    private static RconHttpServer? httpServer;

    /// <summary>
    /// Override to ensure Settings.json is updated with any missing fields
    /// This handles upgrades from older versions of the mod
    /// </summary>
    public override async void Init()
    {
        base.Init();

        // After loading/creating settings, save them back to add any missing fields
        // This ensures old Settings.json files get updated with new default settings
        if (SettingsContainer?.Settings != null)
        {
            // Give the file a moment to settle after initial creation
            await Task.Delay(100);

            // Use reflection to call the protected SaveSettingsAsync method
            // This will serialize all properties with their current values, filling in any missing fields
            var saveMethod = SettingsContainer.GetType().GetMethod("SaveSettingsAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (saveMethod != null)
            {
#pragma warning disable CS8600, CS8602
                var saved = await (Task<bool>)saveMethod.Invoke(SettingsContainer, new object[] { SettingsContainer.Settings })!;
#pragma warning restore CS8600, CS8602
                if (saved)
                    ModManager.Log($"[RCON] Settings.json updated with any missing fields", ModManager.LogLevel.Info);
            }
        }
    }

    /// <summary>
    /// Static reference to the instance so static command handlers can access SettingsContainer
    /// </summary>
    private static PatchClass? Instance = null;

    public override Task OnStartSuccess()
    {
        try
        {
            Settings = SettingsContainer?.Settings ?? new Settings();

            // Store instance reference for static command handler
            Instance = this;

            // Initialize authenticator with settings
            RconAuthenticator.Initialize(Settings);

            if (Settings.RconEnabled)
            {
                // Register custom log4net appender for console log broadcasting
                try
                {
                    var logRepository = log4net.LogManager.GetRepository(System.Reflection.Assembly.GetAssembly(typeof(log4net.LogManager)));
                    var loggerRepository = logRepository as log4net.Repository.Hierarchy.Hierarchy;
                    if (loggerRepository != null)
                    {
                        var rconAppender = new RconLogAppender();
                        rconAppender.ActivateOptions();
                        loggerRepository.Root.AddAppender(rconAppender);
                        ModManager.Log($"[RCON] Console log broadcaster registered");
                    }
                }
                catch (Exception ex)
                {
                    ModManager.Log($"[RCON] WARNING: Failed to register log broadcaster: {ex.Message}", ModManager.LogLevel.Warn);
                }

                // Start TCP RCON server
                ModManager.Log($"[RCON] Starting RCON server on port {Settings.RconPort}...");
                rconServer = new RconServer(Settings);
                rconServer.Start();
                ModManager.Log($"[RCON] RCON server started successfully!");
                ModManager.Log($"[RCON] Listening for RCON connections on port {Settings.RconPort}");
                ModManager.Log($"[RCON] Max connections: {Settings.MaxConnections}");
                ModManager.Log($"[RCON] Verbose logging: {(Settings.EnableLogging ? "enabled" : "disabled")}");

                // Start HTTP/WebSocket server for web client (if enabled)
                if (Settings.WebRconEnabled)
                {
                    ModManager.Log($"[RCON] Starting web client server...");
                    httpServer = new RconHttpServer(Settings);
                    httpServer.Start();
                    ModManager.Log($"[RCON] Web client available at: http://127.0.0.1:9005/");

                    // Initialize the log broadcaster with server references
                    RconLogBroadcaster.Instance.Initialize(rconServer, httpServer, Settings);

                    // Initialize WebSocket handler with HTTP server reference
                    RconWebSocketHandler.Initialize(httpServer);
                }
                else
                {
                    ModManager.Log($"[RCON] Web RCON is disabled. Only TCP RCON available.");

                    // Still initialize broadcaster with TCP server only (no WebSocket server)
                    RconLogBroadcaster.Instance.Initialize(rconServer, null!, Settings);
                }
            }
            else
            {
                ModManager.Log($"[RCON] RCON is disabled in settings");
            }
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR during startup - {ex.Message}", ModManager.LogLevel.Error);
        }

        return Task.CompletedTask;
    }

    public override void Stop()
    {
        try
        {
            // Stop HTTP server
            if (httpServer != null)
            {
                ModManager.Log($"[RCON] Stopping web client server...");
                httpServer.Stop();
                httpServer = null;
            }

            // Stop TCP RCON server
            if (rconServer != null)
            {
                ModManager.Log($"[RCON] Stopping RCON server...");
                rconServer.Stop();
                rconServer = null;
                ModManager.Log($"[RCON] RCON server stopped");
            }
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR during shutdown - {ex.Message}", ModManager.LogLevel.Error);
        }

        base.Stop();
    }

    /// <summary>
    /// Reload RCON settings from Settings.json
    /// Server console command: rcon reload
    /// </summary>
    public static void ReloadRconSettings()
    {
        try
        {
            if (Instance?.SettingsContainer == null)
            {
                ModManager.Log($"[RCON] ERROR: Settings container not available", ModManager.LogLevel.Error);
                return;
            }

            ModManager.Log($"[RCON] Reloading settings from Settings.json...");

            // Reload settings from file using reflection
            var reloadMethod = Instance.SettingsContainer.GetType().GetMethod("LoadSettings",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (reloadMethod != null)
            {
                reloadMethod.Invoke(Instance.SettingsContainer, new object[] { });
                var newSettings = Instance.SettingsContainer.Settings;

                if (newSettings != null)
                {
                    // Update our static Settings reference
                    PatchClass.Settings = newSettings;

                    // Re-initialize authenticator with new settings
                    RconAuthenticator.Initialize(PatchClass.Settings);

                    ModManager.Log($"[RCON] Settings reloaded successfully!");
                    ModManager.Log($"[RCON] DebugMode: {PatchClass.Settings.DebugMode}");
                    ModManager.Log($"[RCON] EnableLogging: {PatchClass.Settings.EnableLogging}");
                    ModManager.Log($"[RCON] RconEnabled: {PatchClass.Settings.RconEnabled}");
                }
                else
                {
                    ModManager.Log($"[RCON] ERROR: Failed to load new settings", ModManager.LogLevel.Error);
                }
            }
            else
            {
                ModManager.Log($"[RCON] ERROR: LoadSettings method not found", ModManager.LogLevel.Error);
            }
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR reloading settings: {ex.Message}", ModManager.LogLevel.Error);
        }
    }

    /// <summary>
    /// Harmony patch to detect player login
    /// Patches Player.PlayerEnterWorld() in ACE.Server.WorldObjects
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.PlayerEnterWorld))]
    public static class Patch_PlayerEnterWorld
    {
        public static void Postfix(Player __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                // Broadcast player login event
                var playerData = new Dictionary<string, object>
                {
                    { "playerName", __instance.Name ?? "Unknown" },
                    { "playerGuid", __instance.Guid.Full },
                    { "level", __instance.Level ?? 0 },
                    { "location", __instance.Location?.ToString() ?? "Unknown" },
                    { "count", GetOnlinePlayerCount() },
                    { "WorldTime", DateTime.UtcNow.ToString("O") }
                };

                RconLogBroadcaster.Instance.BroadcastPlayerEvent("login", playerData);

                if (PatchClass.Settings.EnableLogging)
                    ModManager.Log($"[RCON] Player login detected: {__instance.Name}", ModManager.LogLevel.Info);
            }
            catch (Exception ex)
            {
                ModManager.Log($"[RCON] ERROR in Patch_PlayerEnterWorld: {ex.Message}", ModManager.LogLevel.Error);
            }
        }
    }

    /// <summary>
    /// Harmony patch to detect player logoff
    /// Patches PlayerManager.SwitchPlayerFromOnlineToOffline()
    /// This fires when player is actually removed from online list
    /// </summary>
    [HarmonyPatch]
    public static class Patch_PlayerLogoff
    {
        // Target the SwitchPlayerFromOnlineToOffline method in PlayerManager
        static bool TryGetMethod(out System.Reflection.MethodBase? method)
        {
            method = null;
            try
            {
                var playerManagerType = Type.GetType("ACE.Server.Managers.PlayerManager, ACE.Server");
                if (playerManagerType != null)
                {
                    method = playerManagerType.GetMethod("SwitchPlayerFromOnlineToOffline",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                        null,
                        new[] { typeof(Player) },
                        null);
                    return method != null;
                }
            }
            catch (Exception ex)
            {
                ModManager.Log($"[RCON] ERROR finding SwitchPlayerFromOnlineToOffline method: {ex.Message}", ModManager.LogLevel.Warn);
            }
            return false;
        }

        static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            if (TryGetMethod(out var method) && method != null)
            {
                yield return method;
            }
        }

        public static void Prefix(Player player)
        {
            try
            {
                if (player == null)
                    return;

                ModManager.Log($"[RCON] Patch_PlayerLogoff: Player being removed from online list: {player.Name}", ModManager.LogLevel.Info);

                // Get count RIGHT BEFORE removal
                int currentOnlineCount = GetOnlinePlayerCount();
                int projectedCount = currentOnlineCount - 1; // Will be removed next

                // Broadcast player logoff event with ACTUAL count after removal
                var playerData = new Dictionary<string, object>
                {
                    { "playerName", player.Name ?? "Unknown" },
                    { "playerGuid", player.Guid.Full },
                    { "level", player.Level ?? 0 },
                    { "location", player.Location?.ToString() ?? "Unknown" },
                    { "count", projectedCount },
                    { "WorldTime", DateTime.UtcNow.ToString("O") }
                };

                RconLogBroadcaster.Instance.BroadcastPlayerEvent("logoff", playerData);

                ModManager.Log($"[RCON] Player logoff event broadcast: {player.Name} (count will be: {projectedCount})", ModManager.LogLevel.Info);
            }
            catch (Exception ex)
            {
                ModManager.Log($"[RCON] ERROR in Patch_PlayerLogoff: {ex.Message}", ModManager.LogLevel.Error);
            }
        }
    }


    /// <summary>
    /// Helper method to get current online player count
    /// </summary>
    private static int GetOnlinePlayerCount()
    {
        try
        {
            // Get PlayerManager type and call GetOnlineCount if available
            var playerManagerType = Type.GetType("ACE.Server.Managers.PlayerManager, ACE.Server");
            if (playerManagerType != null)
            {
                var method = playerManagerType.GetMethod("GetOnlineCount", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (method != null)
                {
                    var result = method.Invoke(null, null);
                    if (result is int count)
                        return count;
                }

                // Fallback: try getting onlinePlayers count via reflection
                var onlinePlayersField = playerManagerType.GetField("onlinePlayers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (onlinePlayersField != null)
                {
                    var dict = onlinePlayersField.GetValue(null);
                    var countProperty = dict?.GetType().GetProperty("Count");
                    if (countProperty != null)
                    {
                        var count = countProperty.GetValue(dict);
                        if (count is int intCount)
                            return intCount;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR getting online player count: {ex.Message}", ModManager.LogLevel.Warn);
        }

        return 0;
    }
}

/// <summary>
/// RCON Mod Settings
/// Automatically loaded/saved to Settings.json
/// </summary>
public class Settings
{
    /// <summary>
    /// Enable/disable RCON functionality (TCP and WebSocket)
    /// </summary>
    public bool RconEnabled { get; set; } = true;

    /// <summary>
    /// Enable/disable Web RCON (WebSocket only)
    /// When disabled, only TCP RCON is available
    /// Default: true
    /// </summary>
    public bool WebRconEnabled { get; set; } = true;

    /// <summary>
    /// Port to listen for RCON connections (TCP)
    /// Default: 9004
    /// </summary>
    public int RconPort { get; set; } = 9004;

    /// <summary>
    /// RCON password (used if no ACE admin account available)
    /// Change this to something secure!
    /// </summary>
    public string RconPassword { get; set; } = "change_me_to_secure_password";

    /// <summary>
    /// Maximum concurrent RCON connections allowed
    /// Default: 10
    /// </summary>
    public int MaxConnections { get; set; } = 10;

    /// <summary>
    /// Connection timeout in seconds
    /// Idle connections will be closed after this duration
    /// Default: 300 (5 minutes)
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Enable verbose logging of RCON operations
    /// </summary>
    public bool EnableLogging { get; set; } = false;

    /// <summary>
    /// Enable debug mode - shows full JSON responses in web client
    /// Set to true to see Data objects and detailed response info
    /// </summary>
    public bool DebugMode { get; set; } = false;

    /// <summary>
    /// Auto-refresh players list when players login/logoff
    /// When enabled, player list will update automatically instead of requiring manual refresh
    /// </summary>
    public bool AutoRefreshPlayers { get; set; } = true;

    /// <summary>
    /// Maximum number of reconnection attempts for web client
    /// Default: 42 (allows for ~10.5 minutes of reconnect attempts at 15 sec intervals)
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 42;

    /// <summary>
    /// Delay in milliseconds between reconnection attempts
    /// Default: 15000 (15 seconds)
    /// </summary>
    public int ReconnectDelayMs { get; set; } = 15000;

    /// <summary>
    /// Use ACE-style packet-based authentication instead of Rust-style URL-based authentication
    /// Default: false (Rust-style URL auth: ws://server:port/password)
    /// Set to true for ACE-style auth: password sent in first packet {"Command": "auth", "Password": "xxx"}
    /// When true, web client will show login landing page
    /// </summary>
    public bool UseAceAuthentication { get; set; } = false;
}
