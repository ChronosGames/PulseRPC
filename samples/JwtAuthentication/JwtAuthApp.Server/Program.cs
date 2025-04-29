using System;
using System.Net;
using System.Threading.Tasks;
using JwtAuthApp.Server.Authentication;
using JwtAuthApp.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using PulseRPC.Server;

var builder = WebApplication.CreateBuilder(args);

// 配置PulseRPC服务器
var pulseServer = new PulseServer(new IPEndPoint(IPAddress.Any, 5001),
    builder.Services.BuildServiceProvider().GetRequiredService<ILogger<PulseServer>>());

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

// 注册PulseRPC服务
var serviceProvider = app.Services;
pulseServer.RegisterService(new AccountService(serviceProvider.GetRequiredService<JwtTokenService>()));
pulseServer.RegisterService(new GreeterService());

// 启动PulseRPC服务器
_ = Task.Run(() => pulseServer.StartAsync());

app.UseAuthentication();
app.UseAuthorization();

await app.RunAsync();
