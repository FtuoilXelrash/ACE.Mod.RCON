
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

    /// <summary>
    /// Static reference to the instance so static command handlers can access SettingsContainer
    /// </summary>
    private static PatchClass? Instance = null;

    public override Task OnStartSuccess()
    {
        // Always use defaults - don't depend on SettingsContainer
        Settings = new Settings();

        // Store instance reference for static command handler
        Instance = this;

        try
        {
            // Initialize authenticator with settings
            RconAuthenticator.Initialize(Settings);

            // Register custom log4net appender for console log broadcasting (needed by both TCP and Web)
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

            // Start TCP RCON server (if enabled)
            if (Settings.RconEnabled)
            {
                ModManager.Log($"[RCON] Starting RCON server on port {Settings.RconPort}...");
                rconServer = new RconServer(Settings);
                rconServer.Start();
                ModManager.Log($"[RCON] RCON server started successfully!");
                ModManager.Log($"[RCON] Listening for RCON connections on port {Settings.RconPort}");
                ModManager.Log($"[RCON] Max connections: {Settings.MaxConnections}");
                ModManager.Log($"[RCON] Verbose logging: {(Settings.EnableLogging ? "enabled" : "disabled")}");
            }
            else
            {
                ModManager.Log($"[RCON] TCP RCON is disabled in settings");
            }

            // Start HTTP/WebSocket server for web client (if enabled - can run independently)
            if (Settings.WebRconEnabled)
            {
                ModManager.Log($"[RCON] Starting web client server on port {Settings.WebRconPort}...");
                httpServer = new RconHttpServer(Settings);
                httpServer.Start();
                ModManager.Log($"[RCON] Web client available at: http://127.0.0.1:{Settings.WebRconPort}/");

                // Initialize WebSocket handler with HTTP server reference
                RconWebSocketHandler.Initialize(httpServer);
            }
            else
            {
                ModManager.Log($"[RCON] Web RCON is disabled in settings");
            }

            // Initialize the log broadcaster with whichever servers are running
            RconLogBroadcaster.Instance.Initialize(rconServer, httpServer, Settings);

            ModManager.Log($"[RCON] RCON started successfully!");
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] CRITICAL ERROR in OnStartSuccess(): {ex.GetType().Name}: {ex.Message}", ModManager.LogLevel.Error);
            ModManager.Log($"[RCON] Stack trace: {ex.StackTrace}", ModManager.LogLevel.Error);
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
    public bool RconEnabled { get; set; } = false;
    public bool WebRconEnabled { get; set; } = true;
    public int RconPort { get; set; } = 9004;
    public int WebRconPort { get; set; } = 9005;
    public string RconPassword { get; set; } = "your_secure_password";
    public int MaxConnections { get; set; } = 3;
    public int ConnectionTimeoutSeconds { get; set; } = 300;
    public bool EnableLogging { get; set; } = true;
    public bool DebugMode { get; set; } = false;
    public bool AutoRefreshPlayers { get; set; } = true;
    public int MaxReconnectAttempts { get; set; } = 42;
    public int ReconnectDelayMs { get; set; } = 15000;
    public bool UseAceAuthentication { get; set; } = true;
}
