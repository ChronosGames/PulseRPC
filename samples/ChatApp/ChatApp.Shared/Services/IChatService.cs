using System.Threading.Tasks;

namespace ChatApp.Shared.Services
{
    /// <summary>
    /// Client -> Server API
    /// </summary>
    public interface IChatService
    {
        Task<bool> GenerateException(string message);
        Task<bool> SendReportAsync(string message);
    }
}
