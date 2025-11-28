namespace RCON;

/// <summary>
/// RCON Authentication Handler
/// Supports both:
/// - Rust-style: RCON password only
/// - ACE-style: ACE admin account credentials (username + password)
/// </summary>
public static class RconAuthenticator
{
    private static Settings? settings;

    // AccessLevel enum from ACE
    private enum AccessLevel : uint
    {
        Player = 0,
        Advocate = 1,
        Sentinel = 2,
        Envoy = 3,
        Developer = 4,
        Admin = 5
    }

    /// <summary>
    /// Initialize authenticator with settings
    /// </summary>
    public static void Initialize(Settings rconSettings)
    {
        settings = rconSettings;
    }

    /// <summary>
    /// Authenticate with RCON password (Rust-style)
    /// </summary>
    public static Task<bool> AuthenticateAsync(string? password)
    {
        return Task.FromResult(Authenticate(password));
    }

    /// <summary>
    /// Synchronous authentication with RCON password
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
            ModManager.Log("[RCON] Authentication failed: invalid RCON password", ModManager.LogLevel.Warn);
        }
        else
        {
            ModManager.Log("[RCON] Authentication successful via RCON password", ModManager.LogLevel.Info);
        }

        return isValid;
    }

    /// <summary>
    /// Authenticate with ACE admin account credentials (ACE-style)
    /// </summary>
    public static Task<bool> AuthenticateAceAccountAsync(string? accountName, string? password)
    {
        return Task.FromResult(AuthenticateAceAccount(accountName, password));
    }

    /// <summary>
    /// Authenticate with ACE admin account credentials
    /// Validates username is admin account and password matches
    /// </summary>
    public static bool AuthenticateAceAccount(string? accountName, string? password)
    {
        if (string.IsNullOrEmpty(accountName) || string.IsNullOrEmpty(password))
        {
            ModManager.Log("[RCON] ACE authentication failed: missing accountName or password", ModManager.LogLevel.Warn);
            return false;
        }

        try
        {
            // Look up account in ACE authentication database
            var account = DatabaseManager.Authentication.GetAccountByName(accountName);

            if (account == null)
            {
                ModManager.Log($"[RCON] ACE authentication failed: account '{accountName}' not found", ModManager.LogLevel.Warn);
                return false;
            }

            // Check if account has admin access (AccessLevel >= 4 = Developer or Admin)
            if (account.AccessLevel < (uint)AccessLevel.Developer)
            {
                ModManager.Log($"[RCON] ACE authentication failed: account '{accountName}' is not an admin (AccessLevel: {account.AccessLevel})", ModManager.LogLevel.Warn);
                return false;
            }

            // Verify password using ACE's PasswordMatches method (supports both BCrypt and SHA512)
            bool passwordValid = account.PasswordMatches(password);

            if (!passwordValid)
            {
                ModManager.Log($"[RCON] ACE authentication failed: invalid password for '{accountName}'", ModManager.LogLevel.Warn);
                return false;
            }

            ModManager.Log($"[RCON] ACE authentication successful: '{accountName}' (AccessLevel: {account.AccessLevel})", ModManager.LogLevel.Info);
            return true;
        }
        catch (Exception ex)
        {
            ModManager.Log($"[RCON] ERROR during ACE authentication: {ex.Message}", ModManager.LogLevel.Error);
            return false;
        }
    }

    /// <summary>
    /// Check if user is a server admin (from ACE account)
    /// </summary>
    public static bool IsAdminUser(string accountName)
    {
        try
        {
            var account = DatabaseManager.Authentication.GetAccountByName(accountName);
            if (account == null) return false;
            return account.AccessLevel >= (uint)AccessLevel.Developer;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get admin information for a user (from ACE account)
    /// </summary>
    public static Dictionary<string, object>? GetAdminInfo(string accountName)
    {
        try
        {
            var account = DatabaseManager.Authentication.GetAccountByName(accountName);
            if (account == null) return null;

            return new Dictionary<string, object>
            {
                { "Name", account.AccountName },
                { "AccessLevel", account.AccessLevel },
                { "Authenticated", true }
            };
        }
        catch
        {
            return null;
        }
    }
}
