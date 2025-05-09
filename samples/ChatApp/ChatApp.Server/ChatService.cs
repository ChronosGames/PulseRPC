using ChatApp.Shared.Services;
using Microsoft.Extensions.Logging;

namespace ChatApp.Server;

public class ChatService(ILogger<ChatService> logger) : ServiceBase<IChatService>, IChatService
{
    private readonly ILogger logger = logger;

    public UnaryResult GenerateException(string message)
    {
        throw new System.NotImplementedException();
    }

    public UnaryResult SendReportAsync(string message)
    {
        logger.LogDebug($"{message}");

        return UnaryResult.CompletedResult;
    }
}
