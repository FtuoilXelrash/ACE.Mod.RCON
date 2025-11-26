
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
            var saveMethod = SettingsContainer.GetType().GetMethod("SaveSettingsAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (saveMethod != null)
            {
#pragma warning disable CS8600, CS8602
                await (Task<bool>)saveMethod.Invoke(SettingsContainer, new object[] { SettingsContainer.Settings })!;
#pragma warning restore CS8600, CS8602
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
                // Start TCP RCON server
                ModManager.Log($"[RCON] Starting RCON server on port {Settings.RconPort}...");
                rconServer = new RconServer(Settings);
                rconServer.Start();
                ModManager.Log($"[RCON] RCON server started successfully!");
                ModManager.Log($"[RCON] Listening for RCON connections on port {Settings.RconPort}");
                ModManager.Log($"[RCON] Max connections: {Settings.MaxConnections}");
                ModManager.Log($"[RCON] Verbose logging: {(Settings.EnableLogging ? "enabled" : "disabled")}");

                // Start HTTP/WebSocket server for web client
                ModManager.Log($"[RCON] Starting web client server...");
                httpServer = new RconHttpServer(Settings);
                httpServer.Start();
                ModManager.Log($"[RCON] Web client available at: http://127.0.0.1:9005/");

                // Initialize the log broadcaster with server references
                RconLogBroadcaster.Instance.Initialize(rconServer, httpServer);

                // Initialize WebSocket handler with HTTP server reference
                RconWebSocketHandler.Initialize(httpServer);
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
}

/// <summary>
/// RCON Mod Settings
/// Automatically loaded/saved to Settings.json
/// </summary>
public class Settings
{
    /// <summary>
    /// Enable/disable RCON functionality
    /// </summary>
    public bool RconEnabled { get; set; } = true;

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
}
