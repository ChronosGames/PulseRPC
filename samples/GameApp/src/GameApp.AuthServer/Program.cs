using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using StackExchange.Redis;
using GameApp.AuthServer.Services;
using GameApp.AuthServer.Middleware;
using GameApp.Infrastructure.Extensions;
using GameApp.Infrastructure.Configuration;
using GameApp.AuthServer.Configuration;

var builder = WebApplication.CreateBuilder(args);

// 配置服务
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger 配置
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "GameApp Auth API",
        Version = "v1",
        Description = "GameApp 认证服务 API 文档"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// MongoDB 配置
builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("MongoDB") ??
        throw new InvalidOperationException("MongoDB connection string not configured");
    return new MongoClient(connectionString);
});

builder.Services.AddSingleton(sp =>
{
    var client = sp.GetRequiredService<IMongoClient>();
    return client.GetDatabase("gameapp_dev");
});

// Redis 配置
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    var password = builder.Configuration["Redis:Password"];

    var configuration = ConfigurationOptions.Parse(connectionString);
    if (!string.IsNullOrEmpty(password))
    {
        configuration.Password = password;
    }

    return ConnectionMultiplexer.Connect(configuration);
});

// JWT 配置
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

// JWT 认证配置
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ??
    throw new InvalidOperationException("JWT configuration not found");

if (string.IsNullOrEmpty(jwtOptions.SecretKey))
    throw new InvalidOperationException("JWT secret key not configured");

var key = Encoding.ASCII.GetBytes(jwtOptions.SecretKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = jwtOptions.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtOptions.Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

// 限流配置
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("LoginPolicy", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 5;
        opt.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 2;
    });

    options.AddFixedWindowLimiter("ApiPolicy", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 100;
        opt.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 10;
    });
});

// 应用服务
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IZoneService, ZoneService>();
builder.Services.AddScoped<IGameTicketService, GameTicketService>();

// 基础设施服务
builder.Services.AddGameAppInfrastructure(builder.Configuration);

// CORS 配置
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// 健康检查
builder.Services.AddHealthChecks()
    .AddMongoDb(builder.Configuration.GetConnectionString("MongoDB")!)
    .AddRedis(builder.Configuration.GetConnectionString("Redis")!);

var app = builder.Build();

// 配置中间件管道
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "GameApp Auth API v1");
        c.RoutePrefix = string.Empty; // 设置 Swagger UI 为根路径
    });
}

// 自定义中间件
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.UseCors("AllowAll");
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// 健康检查端点
app.MapHealthChecks("/health");
app.MapGet("/ready", () => "Ready");

// 启动时初始化服务
await using var scope = app.Services.CreateAsyncScope();
var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

try
{
    logger.LogInformation("GameApp AuthServer 正在启动...");

    // 测试 MongoDB 连接
    var mongoClient = scope.ServiceProvider.GetRequiredService<IMongoClient>();
    await mongoClient.ListDatabaseNamesAsync();
    logger.LogInformation("MongoDB 连接成功");

    // 测试 Redis 连接
    var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
    var db = redis.GetDatabase();
    await db.PingAsync();
    logger.LogInformation("Redis 连接成功");

    logger.LogInformation("GameApp AuthServer 启动完成，监听端口: {Port}",
        builder.Configuration["ASPNETCORE_URLS"] ?? "http://localhost:8080");
}
catch (Exception ex)
{
    logger.LogError(ex, "GameApp AuthServer 启动失败");
    throw;
}

await app.RunAsync();
