using System.Threading.Tasks;
using PulseRPC;

namespace JwtAuthApp.Shared
{
    public interface IGreeterService : IPulseService<IGreeterService>
    {
        Task<PulseResult<string>> HelloAsync();
        Task<PulseResult<string>> ServerAsync(string name, int age);
        Task<PulseResult<string>> ClientAsync(int[] items);
        Task<PulseResult<string>> DuplexAsync(int[] items);
    }
}
