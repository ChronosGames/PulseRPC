using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sentry;
using Sentry.Extensions.Logging;

namespace DistributedGameApp.Infrastructure.Sentry.Extensions;

/// <summary>
/// Sentry 依赖注入扩展
/// </summary>
public static class SentryServiceCollectionExtensions
{
    /// <summary>
    /// 添加 Sentry 日志记录
    /// </summary>
    public static IServiceCollection AddSentryLogging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var sentryOptions = new SentryOptions();
        configuration.GetSection(SentryOptions.SectionName).Bind(sentryOptions);

        if (!sentryOptions.Enabled || string.IsNullOrEmpty(sentryOptions.Dsn))
        {
            return services;
        }

        // 添加 Sentry 到日志系统
        services.AddLogging(builder =>
        {
            builder.AddSentry(options =>
            {
                options.Dsn = sentryOptions.Dsn;
                options.Environment = sentryOptions.Environment;
                options.SampleRate = (float)sentryOptions.SampleRate;
                options.TracesSampleRate = sentryOptions.TracesSampleRate;
                options.SendDefaultPii = sentryOptions.SendDefaultPii;
                options.MaxBreadcrumbs = sentryOptions.MaxBreadcrumbs;
                options.AttachStacktrace = sentryOptions.AttachStacktrace;
                options.MinimumBreadcrumbLevel = LogLevel.Debug;
                options.MinimumEventLevel = LogLevel.Error;

                // 设置版本信息
                options.Release = GetAssemblyVersion();
            });
        });

        return services;
    }

    /// <summary>
    /// 添加 Sentry（使用自定义配置）
    /// </summary>
    public static IServiceCollection AddSentryLogging(
        this IServiceCollection services,
        Action<SentryOptions> configureOptions)
    {
        var sentryOptions = new SentryOptions();
        configureOptions(sentryOptions);

        if (!sentryOptions.Enabled || string.IsNullOrEmpty(sentryOptions.Dsn))
        {
            return services;
        }

        services.AddLogging(builder =>
        {
            builder.AddSentry(options =>
            {
                options.Dsn = sentryOptions.Dsn;
                options.Environment = sentryOptions.Environment;
                options.SampleRate = (float)sentryOptions.SampleRate;
                options.TracesSampleRate = sentryOptions.TracesSampleRate;
                options.SendDefaultPii = sentryOptions.SendDefaultPii;
                options.MaxBreadcrumbs = sentryOptions.MaxBreadcrumbs;
                options.AttachStacktrace = sentryOptions.AttachStacktrace;
                options.MinimumBreadcrumbLevel = LogLevel.Debug;
                options.MinimumEventLevel = LogLevel.Error;
                options.Release = GetAssemblyVersion();
            });
        });

        return services;
    }

    private static string GetAssemblyVersion()
    {
        var assembly = System.Reflection.Assembly.GetEntryAssembly();
        var version = assembly?.GetName().Version;
        return version?.ToString() ?? "unknown";
    }
}
