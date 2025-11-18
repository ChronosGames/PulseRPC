using DistributedGameApp.Client;
using DistributedGameApp.Shared.Messages;
using Microsoft.Extensions.Logging;

// ====================================================================
// DistributedGameApp 客户端
// 基于 PulseRPC 的分布式游戏客户端
// ====================================================================

Console.WriteLine("╔════════════════════════════════════════════════════════╗");
Console.WriteLine("║     DistributedGameApp 客户端                          ║");
Console.WriteLine("║     基于 PulseRPC 的分布式游戏                         ║");
Console.WriteLine("╚════════════════════════════════════════════════════════╝");
Console.WriteLine();

// 解析命令行参数
var loginServerUrl = args.Length > 0 ? args[0] : "http://localhost:5000";
var runMode = args.Length > 1 ? args[1] : "interactive";

Console.WriteLine($"LoginServer URL: {loginServerUrl}");
Console.WriteLine($"运行模式: {runMode}");
Console.WriteLine();

// 创建日志工厂
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

try
{
    switch (runMode.ToLower())
    {
        case "full":
            // 完整流程示例
            Console.WriteLine("运行完整流程示例...\n");
            await GameClientExample.RunFullFlowExampleAsync();
            break;

        case "quick":
            // 快速开始示例
            Console.WriteLine("运行快速开始示例...\n");
            await GameClientExample.RunQuickStartExampleAsync();
            break;

        case "login":
            // 仅登录和服务器列表
            Console.WriteLine("运行登录示例...\n");
            await GameClientExample.RunLoginAndServerListExampleAsync();
            break;

        case "interactive":
        default:
            // 交互式模式
            await RunInteractiveModeAsync(loginServerUrl, loggerFactory);
            break;
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\n发生错误: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
    Console.ResetColor();
}

Console.WriteLine("\n按任意键退出...");
Console.ReadKey(true);

// ====================================================================
// 交互式模式
// ====================================================================

static async Task RunInteractiveModeAsync(string loginServerUrl, ILoggerFactory loggerFactory)
{
    using var client = new GameClient(loginServerUrl, loggerFactory);

    ShowWelcome();
    ShowHelp();

    var running = true;
    while (running)
    {
        Console.Write("> ");
        var input = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(input))
            continue;

        var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            continue;

        var command = parts[0].ToLower();

        try
        {
            switch (command)
            {
                case "help":
                case "h":
                case "?":
                    ShowHelp();
                    break;

                case "register":
                    if (parts.Length < 4)
                    {
                        Console.WriteLine("用法: register <用户名> <密码> <邮箱>");
                    }
                    else
                    {
                        await client.RegisterAsync(parts[1], parts[2], parts[3]);
                    }
                    break;

                case "login":
                    if (parts.Length < 3)
                    {
                        Console.WriteLine("用法: login <用户名/邮箱> <密码>");
                    }
                    else
                    {
                        await client.LoginAsync(parts[1], parts[2]);
                    }
                    break;

                case "servers":
                case "listservers":
                    {
                        var servers = await client.GetGameServerListAsync();
                        Console.WriteLine($"\n可用服务器 ({servers.Count}):");
                        for (int i = 0; i < servers.Count; i++)
                        {
                            var s = servers[i];
                            Console.WriteLine($"  [{i + 1}] {s.ServerName}");
                            Console.WriteLine($"      地址: {s.Host}:{s.TcpPort}");
                            Console.WriteLine($"      负载: {s.CurrentPlayers}/{s.MaxPlayers} ({s.LoadPercentage}%)");
                            Console.WriteLine($"      状态: {s.Status}");
                        }
                        Console.WriteLine();
                    }
                    break;

                case "recommend":
                    {
                        var server = await client.GetRecommendedGameServerAsync();
                        if (server != null)
                        {
                            Console.WriteLine($"\n推荐服务器: {server.ServerName}");
                            Console.WriteLine($"  地址: {server.Host}:{server.TcpPort}");
                            Console.WriteLine($"  负载: {server.CurrentPlayers}/{server.MaxPlayers} ({server.LoadPercentage}%)");
                            Console.WriteLine();
                        }
                    }
                    break;

                case "connect":
                    if (parts.Length < 2)
                    {
                        // 连接到推荐服务器
                        var server = await client.GetRecommendedGameServerAsync();
                        if (server != null)
                        {
                            await client.ConnectToGameServerAsync(server);
                        }
                    }
                    else
                    {
                        // 通过索引连接
                        if (int.TryParse(parts[1], out var index))
                        {
                            var servers = await client.GetGameServerListAsync();
                            if (index > 0 && index <= servers.Count)
                            {
                                await client.ConnectToGameServerAsync(servers[index - 1]);
                            }
                            else
                            {
                                Console.WriteLine($"无效的服务器索引: {index}");
                            }
                        }
                        else
                        {
                            Console.WriteLine("用法: connect [服务器索引]");
                        }
                    }
                    break;

                case "characters":
                case "chars":
                    {
                        var characters = await client.GetCharacterListAsync();
                        Console.WriteLine($"\n角色列表 ({characters.Count}):");
                        for (int i = 0; i < characters.Count; i++)
                        {
                            var c = characters[i];
                            Console.WriteLine($"  [{i + 1}] {c.CharacterName}");
                            Console.WriteLine($"      职业: {c.Class}  等级: {c.Level}");
                            Console.WriteLine($"      HP: {c.Hp}/{c.MaxHp}  攻击: {c.Attack}  防御: {c.Defense}");
                        }
                        Console.WriteLine();
                    }
                    break;

                case "create":
                case "createchar":
                    if (parts.Length < 4)
                    {
                        Console.WriteLine("用法: create <名称> <职业> <性别>");
                        Console.WriteLine("职业: Warrior, Mage, Archer, Assassin, Priest");
                        Console.WriteLine("性别: Male, Female");
                    }
                    else
                    {
                        if (Enum.TryParse<CharacterClass>(parts[2], true, out var charClass) &&
                            Enum.TryParse<Gender>(parts[3], true, out var gender))
                        {
                            await client.CreateCharacterAsync(parts[1], charClass, gender);
                        }
                        else
                        {
                            Console.WriteLine("无效的职业或性别");
                        }
                    }
                    break;

                case "select":
                case "selectchar":
                    if (parts.Length < 2)
                    {
                        Console.WriteLine("用法: select <角色索引>");
                    }
                    else if (int.TryParse(parts[1], out var index))
                    {
                        var characters = await client.GetCharacterListAsync();
                        if (index > 0 && index <= characters.Count)
                        {
                            await client.SelectCharacterAsync(characters[index - 1].CharacterId);
                        }
                        else
                        {
                            Console.WriteLine($"无效的角色索引: {index}");
                        }
                    }
                    break;

                case "delete":
                case "deletechar":
                    if (parts.Length < 2)
                    {
                        Console.WriteLine("用法: delete <角色索引>");
                    }
                    else if (int.TryParse(parts[1], out var index))
                    {
                        var characters = await client.GetCharacterListAsync();
                        if (index > 0 && index <= characters.Count)
                        {
                            await client.DeleteCharacterAsync(characters[index - 1].CharacterId);
                        }
                        else
                        {
                            Console.WriteLine($"无效的角色索引: {index}");
                        }
                    }
                    break;

                case "match":
                    if (parts.Length < 2)
                    {
                        Console.WriteLine("用法: match <模式>");
                        Console.WriteLine("模式: OneVsOne, ThreeVsThree, FiveVsFive");
                    }
                    else if (Enum.TryParse<MatchMode>(parts[1], true, out var mode))
                    {
                        await client.RequestMatchAsync(mode);
                    }
                    else
                    {
                        Console.WriteLine("无效的匹配模式");
                    }
                    break;

                case "ready":
                    await client.BattleReadyAsync();
                    break;

                case "leave":
                case "leavebattle":
                    await client.LeaveBattleAsync();
                    break;

                case "status":
                case "info":
                    client.DisplayStatus();
                    break;

                case "clear":
                case "cls":
                    Console.Clear();
                    ShowWelcome();
                    break;

                case "exit":
                case "quit":
                case "q":
                    running = false;
                    break;

                default:
                    Console.WriteLine($"未知命令: {command}，输入 'help' 查看帮助");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"错误: {ex.Message}");
            Console.ResetColor();
        }
    }
}

static void ShowWelcome()
{
    Console.WriteLine("欢迎使用 DistributedGameApp 客户端！");
    Console.WriteLine("这是一个完整的分布式游戏客户端示例。\n");
}

static void ShowHelp()
{
    Console.WriteLine("=== 命令列表 ===");
    Console.WriteLine();

    Console.WriteLine("【认证】");
    Console.WriteLine("  register <用户名> <密码> <邮箱>  - 注册新账号");
    Console.WriteLine("  login <用户名/邮箱> <密码>       - 登录");
    Console.WriteLine();

    Console.WriteLine("【服务器】");
    Console.WriteLine("  servers | listservers           - 列出所有可用服务器");
    Console.WriteLine("  recommend                       - 获取推荐服务器");
    Console.WriteLine("  connect [索引]                  - 连接到服务器（不提供索引则连接推荐服务器）");
    Console.WriteLine();

    Console.WriteLine("【角色管理】");
    Console.WriteLine("  characters | chars              - 显示角色列表");
    Console.WriteLine("  create <名称> <职业> <性别>     - 创建新角色");
    Console.WriteLine("    职业: Warrior, Mage, Archer, Assassin, Priest");
    Console.WriteLine("    性别: Male, Female");
    Console.WriteLine("  select <索引>                   - 选择角色");
    Console.WriteLine("  delete <索引>                   - 删除角色");
    Console.WriteLine();

    Console.WriteLine("【匹配与战斗】");
    Console.WriteLine("  match <模式>                    - 请求匹配");
    Console.WriteLine("    模式: OneVsOne, ThreeVsThree, FiveVsFive");
    Console.WriteLine("  ready                           - 战斗准备");
    Console.WriteLine("  leave | leavebattle             - 离开战斗");
    Console.WriteLine();

    Console.WriteLine("【其他】");
    Console.WriteLine("  status | info                   - 显示客户端状态");
    Console.WriteLine("  clear | cls                     - 清屏");
    Console.WriteLine("  help | h | ?                    - 显示帮助");
    Console.WriteLine("  exit | quit | q                 - 退出程序");
    Console.WriteLine();
}
