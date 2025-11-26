
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
                await (Task<bool>)saveMethod.Invoke(SettingsContainer, new object[] { SettingsContainer.Settings });
            }
        }
    }

    public override Task OnStartSuccess()
    {
        try
        {
            Settings = SettingsContainer?.Settings ?? new Settings();

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
}
