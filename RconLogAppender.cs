namespace RCON;

/// <summary>
/// Custom log4net appender that broadcasts server logs to RCON clients
/// This appender intercepts all server log messages and sends them to connected RCON clients
/// </summary>
public class RconLogAppender : log4net.Appender.AppenderSkeleton
{
    /// <summary>
    /// Append a logging event - called by log4net for every log message
    /// </summary>
    protected override void Append(log4net.Core.LoggingEvent loggingEvent)
    {
        if (loggingEvent == null)
            return;

        try
        {
            // Skip RCON's own logs to avoid infinite loops
            if (loggingEvent.LoggerName != null && loggingEvent.LoggerName.StartsWith("RCON"))
                return;

            // Get the log message
            string message = loggingEvent.RenderedMessage;
            if (string.IsNullOrEmpty(message))
                return;

            // Map log4net level to our level
            var level = loggingEvent.Level.Name.ToLowerInvariant();

            // Broadcast the log message to RCON clients
            try
            {
                RconLogBroadcaster.Instance.BroadcastLogMessage($"[{loggingEvent.LoggerName}] {message}", ConvertLevel(loggingEvent.Level));
            }
            catch
            {
                // Silently fail if broadcaster isn't initialized yet (during startup)
            }
        }
        catch (Exception ex)
        {
            // Don't log errors from this appender to avoid recursion
            System.Diagnostics.Debug.WriteLine($"[RconLogAppender] Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Convert log4net Level to ModManager.LogLevel
    /// </summary>
    private ModManager.LogLevel ConvertLevel(log4net.Core.Level level)
    {
        return level.Name switch
        {
            "DEBUG" => ModManager.LogLevel.Debug,
            "INFO" => ModManager.LogLevel.Info,
            "WARN" => ModManager.LogLevel.Warn,
            "ERROR" => ModManager.LogLevel.Error,
            "FATAL" => ModManager.LogLevel.Fatal,
            _ => ModManager.LogLevel.Info
        };
    }

    /// <summary>
    /// This appender doesn't require a layout
    /// </summary>
    protected override bool RequiresLayout => false;
}
