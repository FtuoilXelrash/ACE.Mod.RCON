
namespace RCON;

[HarmonyPatch]
public class PatchClass(BasicMod mod, string settingsName = "Settings.json") : BasicPatch<Settings>(mod, settingsName)
{
    private static RconServer? rconServer;

    public override Task OnStartSuccess()
    {
        try
        {
            Settings = SettingsContainer?.Settings ?? new Settings();

            // Initialize authenticator with settings
            RconAuthenticator.Initialize(Settings);

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
    /// Port to listen for RCON connections
    /// Default: 2947
    /// </summary>
    public int RconPort { get; set; } = 2947;

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
}
