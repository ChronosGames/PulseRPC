using Microsoft.Extensions.Logging;

namespace ChatApp.Server;

public static class LoggerExtensions
{
    public static void Debug(this ILogger logger, string message)
    {
        logger.LogDebug(message);
    }

    public static void Info(this ILogger logger, string message)
    {
        logger.LogInformation(message);
    }

    public static void Warning(this ILogger logger, string message)
    {
        logger.LogWarning(message);
    }

    public static void Error(this ILogger logger, string message)
    {
        logger.LogError(message);
    }
}
