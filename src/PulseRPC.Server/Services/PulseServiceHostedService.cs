using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Hubs; using PulseRPC.Server.Services; using PulseRPC.Server.Transport;

namespace PulseRPC.Server.Services;

/// <summary>
/// 旧版单实例 IPulseService 托管适配器。
/// </summary>
/// <typeparam name="TService">需要启动的服务类型</typeparam>
[Obsolete(
    "Register managed state with AddPulseService<TService> and resolve it through IServiceAccessor<TService>. PulseServiceManager owns startup and disposal.",
    false)]
public class PulseServiceHostedService<TService> : IHostedService
    where TService : class, IPulseService
{
    private readonly TService _service;
    private readonly ILogger<PulseServiceHostedService<TService>> _logger;

    public PulseServiceHostedService(
        TService service,
        ILogger<PulseServiceHostedService<TService>> logger)
    {
        _service = service;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "正在启动 PulseService: {ServiceType} (ServiceId: {ServiceId})",
                _service.ServiceType,
                _service.ServiceId);

            // 调用 StartAsync 以触发 OnStartAsync
            await _service.StartAsync(cancellationToken);

            _logger.LogInformation(
                "PulseService 启动成功: {ServiceType} (ServiceId: {ServiceId})",
                _service.ServiceType,
                _service.ServiceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "启动 PulseService 失败: {ServiceType} (ServiceId: {ServiceId})",
                _service.ServiceType,
                _service.ServiceId);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "正在停止 PulseService: {ServiceType} (ServiceId: {ServiceId})",
                _service.ServiceType,
                _service.ServiceId);

            await _service.StopAsync(cancellationToken);

            _logger.LogInformation(
                "PulseService 已停止: {ServiceType} (ServiceId: {ServiceId})",
                _service.ServiceType,
                _service.ServiceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "停止 PulseService 失败: {ServiceType} (ServiceId: {ServiceId})",
                _service.ServiceType,
                _service.ServiceId);
        }
    }
}
