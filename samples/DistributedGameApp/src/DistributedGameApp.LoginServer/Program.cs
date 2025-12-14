using DistributedGameApp.Infrastructure.Consul;
using DistributedGameApp.Infrastructure.Consul.Extensions;
using DistributedGameApp.Infrastructure.Hosting;
using DistributedGameApp.Infrastructure.MongoDB.Extensions;
using DistributedGameApp.Infrastructure.Sentry.Extensions;
using DistributedGameApp.LoginServer.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using PulseRPC.Server.Observability;
using System.Text;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// 配置Sentry
if (builder.Configuration.GetValue<bool>("Sentry:Enabled"))
{
    builder.Services.AddSentryLogging(builder.Configuration);
}

// 添加服务器标识（用于Consul注册）
var identity = builder.Configuration.GetSection("ServerIdentity").Get<ServerIdentityOptions>()
    ?? throw new InvalidOperationException("ServerIdentity configuration is missing.");
builder.Services.AddSingleton(identity);

// 添加控制器
builder.Services.AddControllers();

// 添加Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "DistributedGameApp LoginServer API",
        Version = "v1",
        Description = "登录服务器 API - 提供用户认证和服务器列表功能"
    });

    // 添加JWT认证
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    // options.AddSecurityRequirement(new OpenApiSecurityRequirement
    // {
    //     {
    //         new OpenApiSecurityScheme
    //         {
    //             Reference = new OpenApiReference
    //             {
    //                 Type = ReferenceType.SecurityScheme,
    //                 Id = "Bearer"
    //             }
    //         },
    //         Array.Empty<string>()
    //     }
    // });

    // 2. 添加安全要求（使用 OpenApiSecuritySchemeReference）
    options.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", doc)] = []
    });
});

// 配置JWT
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

// 添加JWT认证
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// 添加基础设施服务
builder.Services.AddMongoDb(builder.Configuration);
builder.Services.AddConsul(builder.Configuration);

// 添加后台服务：Consul 服务注册
builder.Services.AddHostedService<ConsulServiceRegistrationService>();

// 添加应用服务
builder.Services.AddSingleton<JwtService>();

// 配置 OpenTelemetry Prometheus Metrics
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        // 添加 PulseRPC 服务指标（如果有）
        metrics.AddMeter(ServiceMetrics.MeterName);

        // 添加 ASP.NET Core 指标
        metrics.AddAspNetCoreInstrumentation();

        // 添加 .NET 运行时指标
        metrics.AddRuntimeInstrumentation();

        // 添加 Prometheus 导出器
        metrics.AddPrometheusExporter();
    });

// 配置CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// 配置HTTP请求管道
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "LoginServer API v1");
        options.RoutePrefix = string.Empty; // 设置Swagger UI为根路径
    });
}

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// 健康检查端点
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    service = "LoginServer"
}));

// Prometheus metrics 端点
app.UseOpenTelemetryPrometheusScrapingEndpoint("/metrics");

app.Logger.LogInformation("LoginServer starting on {Url}", builder.Configuration["Kestrel:Endpoints:Http:Url"]);

app.Run();
