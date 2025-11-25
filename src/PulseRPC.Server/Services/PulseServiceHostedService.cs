using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Abstractions;

namespace PulseRPC.Server.Services;

/// <summary>
/// 通用的 IPulseService 启动托管服务
/// 负责在应用启动时发现并启动所有实现了 IPulseService 的服务
/// </summary>
/// <typeparam name="TService">需要启动的服务类型</typeparam>
public class PulseServiceHostedService<TService> : IHostedService
    where TService : class, IPulseService, IService
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
                "正在启动 PulseService: {ServiceName} (ServiceId: {ServiceId})",
                _service.ServiceName,
                _service.ServiceId);

            // 调用 StartAsync 以触发 OnStartAsync
            await _service.StartAsync(cancellationToken);

            _logger.LogInformation(
                "PulseService 启动成功: {ServiceName} (ServiceId: {ServiceId})",
                _service.ServiceName,
                _service.ServiceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "启动 PulseService 失败: {ServiceName} (ServiceId: {ServiceId})",
                _service.ServiceName,
                _service.ServiceId);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "正在停止 PulseService: {ServiceName} (ServiceId: {ServiceId})",
                _service.ServiceName,
                _service.ServiceId);

            await _service.StopAsync(cancellationToken);

            _logger.LogInformation(
                "PulseService 已停止: {ServiceName} (ServiceId: {ServiceId})",
                _service.ServiceName,
                _service.ServiceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "停止 PulseService 失败: {ServiceName} (ServiceId: {ServiceId})",
                _service.ServiceName,
                _service.ServiceId);
        }
    }
}
