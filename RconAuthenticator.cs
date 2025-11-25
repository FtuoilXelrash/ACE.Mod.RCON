namespace RCON;

/// <summary>
/// RCON Authentication Handler
/// Validates credentials against RCON password
/// Future: Can be extended to use ACE server admin accounts
/// </summary>
public static class RconAuthenticator
{
    private static Settings? settings;

    /// <summary>
    /// Initialize authenticator with settings
    /// </summary>
    public static void Initialize(Settings rconSettings)
    {
        settings = rconSettings;
    }

    /// <summary>
    /// Authenticate with RCON password
    /// </summary>
    public static Task<bool> AuthenticateAsync(string? password)
    {
        return Task.FromResult(Authenticate(password));
    }

    /// <summary>
    /// Synchronous authentication (calls async version)
    /// </summary>
    public static bool Authenticate(string? password)
    {
        if (settings == null)
        {
            ModManager.Log("[RCON] ERROR: Authenticator not initialized", ModManager.LogLevel.Error);
            return false;
        }

        if (string.IsNullOrEmpty(password))
        {
            ModManager.Log("[RCON] Authentication failed: empty password", ModManager.LogLevel.Warn);
            return false;
        }

        // Compare with configured password
        bool isValid = password == settings.RconPassword;

        if (!isValid)
        {
            ModManager.Log("[RCON] Authentication failed: invalid password", ModManager.LogLevel.Warn);
        }
        else
        {
            ModManager.Log("[RCON] Authentication successful", ModManager.LogLevel.Info);
        }

        return isValid;
    }

    /// <summary>
    /// Check if user is a server admin
    /// Future: Implement ACE admin account integration
    /// </summary>
    public static bool IsAdminUser(string accountName)
    {
        // TODO: Implement admin account lookup
        // For now, just password-based authentication
        return true;
    }

    /// <summary>
    /// Get admin information for a user
    /// Future: Implement ACE account lookup
    /// </summary>
    public static Dictionary<string, object>? GetAdminInfo(string accountName)
    {
        // TODO: Implement admin info lookup from ACE accounts
        return new Dictionary<string, object>
        {
            { "Name", accountName },
            { "AccessLevel", 4 }, // Admin
            { "Authenticated", true }
        };
    }
}
