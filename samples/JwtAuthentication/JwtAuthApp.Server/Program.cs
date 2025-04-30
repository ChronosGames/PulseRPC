using System;
using System.Net;
using System.Threading.Tasks;
using JwtAuthApp.Server.Authentication;
using JwtAuthApp.Server.Services;
using JwtAuthApp.Server.Hubs;
using JwtAuthApp.Shared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using PulseRPC.Server;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// 将日志服务添加到容器
builder.Services.AddLogging();

// 注册服务
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.Configure<JwtTokenServiceOptions>(builder.Configuration.GetSection("JwtAuthApp.Server:JwtTokenService"));

// 配置身份验证
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(builder.Configuration.GetSection("JwtAuthApp.Server:JwtTokenService:Secret").Value!)),
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(10),

            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
        };
#if DEBUG
        options.RequireHttpsMetadata = false;
#endif
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// 获取服务提供程序
var serviceProvider = app.Services;

// 配置PulseRPC服务器
var logger = serviceProvider.GetRequiredService<ILogger<PulseServer>>();
var pulseServer = new PulseServer(new IPEndPoint(IPAddress.Any, 5001), logger);

// 添加请求拦截器设置当前用户
pulseServer.OnBeforeRequest += (request, user) =>
{
    // 在每个请求处理前设置当前用户
    AccountService.CurrentUser.Value = user;
    return Task.CompletedTask;
};

// 注册PulseRPC服务
pulseServer.RegisterService(new AccountService(serviceProvider.GetRequiredService<JwtTokenService>()));
pulseServer.RegisterService(new GreeterService());
pulseServer.RegisterHub<ITimerHub, ITimerHubReceiver>(typeof(TimerHub));

// 启动PulseRPC服务器
_ = Task.Run(() => pulseServer.StartAsync());

app.UseAuthentication();
app.UseAuthorization();

await app.RunAsync();
