using DistributedGameApp.Shared.Messages;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.Client;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("================================");
        Console.WriteLine("  DistributedGameApp 客户端");
        Console.WriteLine("  基于 PulseRPC 的分布式游戏");
        Console.WriteLine("================================");
        Console.WriteLine();

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var client = new DistributedGameClient(loggerFactory);

        try
        {
            // 解析命令行参数
            string host = "localhost";
            int port = 8080;

            if (args.Length >= 2)
            {
                host = args[0];
                if (!int.TryParse(args[1], out port))
                {
                    Console.WriteLine($"警告: 无效的端口号 '{args[1]}'，使用默认端口 8080");
                    port = 8080;
                }
            }

            // 初始化客户端
            await client.InitializeAsync(host, port);

            // 启动命令循环
            await RunCommandLoopAsync(client);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n错误: {ex.Message}");
            Console.ResetColor();
        }
        finally
        {
            // 清理资源
            await client.ShutdownAsync();
            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey(true);
        }
    }

    static async Task RunCommandLoopAsync(DistributedGameClient client)
    {
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
                        ShowHelp();
                        break;

                    case "login":
                        if (parts.Length < 3)
                        {
                            Console.WriteLine("用法: login <账号> <密码>");
                        }
                        else
                        {
                            await client.LoginAsync(parts[1], parts[2]);
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

                    case "info":
                    case "status":
                        client.DisplayStatus();
                        break;

                    case "player":
                    case "playerinfo":
                        await client.GetPlayerInfoAsync();
                        break;

                    case "move":
                        if (parts.Length < 4)
                        {
                            Console.WriteLine("用法: move <x> <y> <z>");
                        }
                        else if (float.TryParse(parts[1], out var x) &&
                                 float.TryParse(parts[2], out var y) &&
                                 float.TryParse(parts[3], out var z))
                        {
                            await client.MoveAsync(x, y, z);
                        }
                        else
                        {
                            Console.WriteLine("无效的坐标");
                        }
                        break;

                    case "join":
                    case "joinroom":
                        if (parts.Length < 3)
                        {
                            Console.WriteLine("用法: join <房间ID> <玩家名称>");
                        }
                        else
                        {
                            await client.JoinChatRoomAsync(parts[1], parts[2]);
                        }
                        break;

                    case "say":
                    case "chat":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("用法: say <消息内容>");
                        }
                        else
                        {
                            var message = string.Join(' ', parts.Skip(1));
                            await client.SendMessageAsync(message);
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

                    case "battle":
                    case "joinbattle":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("用法: battle <战斗ID>");
                        }
                        else
                        {
                            await client.JoinBattleAsync(parts[1]);
                        }
                        break;

                    case "ready":
                        await client.BattleReadyAsync();
                        break;

                    case "attack":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("用法: attack <目标角色ID>");
                        }
                        else
                        {
                            await client.PerformAttackAsync(parts[1]);
                        }
                        break;

                    case "skill":
                        if (parts.Length < 3)
                        {
                            Console.WriteLine("用法: skill <技能ID> <目标角色ID>");
                        }
                        else
                        {
                            await client.UseSkillAsync(parts[1], parts[2]);
                        }
                        break;

                    case "servers":
                    case "listservers":
                        client.ListServers();
                        break;

                    case "switch":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("用法: switch <服务器ID>");
                        }
                        else
                        {
                            var success = client.SwitchServer(parts[1]);
                            if (success)
                            {
                                Console.WriteLine($"已切换到服务器: {parts[1]}");
                            }
                            else
                            {
                                Console.WriteLine($"切换失败: 服务器 {parts[1]} 不存在或未连接");
                            }
                        }
                        break;

                    case "connectgame":
                        if (parts.Length < 4)
                        {
                            Console.WriteLine("用法: connectgame <服务器ID> <名称> <主机> <端口>");
                        }
                        else if (int.TryParse(parts[3], out var gamePort))
                        {
                            await client.ConnectToGameServerAsync(parts[1], parts[2], parts[2], gamePort);
                        }
                        else
                        {
                            Console.WriteLine("无效的端口号");
                        }
                        break;

                    case "connectbattle":
                        if (parts.Length < 4)
                        {
                            Console.WriteLine("用法: connectbattle <战斗ID> <主机> <端口>");
                        }
                        else if (int.TryParse(parts[3], out var battlePort))
                        {
                            await client.ConnectToBattleServerAsync(parts[1], parts[2], battlePort);
                        }
                        else
                        {
                            Console.WriteLine("无效的端口号");
                        }
                        break;

                    case "scenario":
                    case "runscenario":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("用法: scenario fullbattle <账号> <角色名> <职业>");
                            Console.WriteLine("职业: Warrior, Mage, Archer, Assassin, Priest");
                        }
                        else
                        {
                            await RunScenarioCommandAsync(client, parts);
                        }
                        break;

                    case "leave":
                    case "leavebattle":
                        await client.LeaveBattleAsync();
                        break;

                    case "exit":
                    case "quit":
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

    static void ShowHelp()
    {
        Console.WriteLine("\n=== 命令列表 ===");
        Console.WriteLine("账号管理:");
        Console.WriteLine("  login <账号> <密码>                   - 登录账号");
        Console.WriteLine("  create <名称> <职业> <性别>           - 创建角色");
        Console.WriteLine();
        Console.WriteLine("服务器管理:");
        Console.WriteLine("  servers | listservers                 - 列出所有已连接的服务器");
        Console.WriteLine("  switch <服务器ID>                     - 切换到指定服务器");
        Console.WriteLine("  connectgame <ID> <名称> <主机> <端口> - 连接到额外的 GameServer");
        Console.WriteLine("  connectbattle <战斗ID> <主机> <端口>  - 连接到 BattleServer");
        Console.WriteLine();
        Console.WriteLine("玩家操作:");
        Console.WriteLine("  info | status                         - 显示客户端状态");
        Console.WriteLine("  player | playerinfo                   - 获取玩家信息");
        Console.WriteLine("  move <x> <y> <z>                      - 移动到指定位置");
        Console.WriteLine();
        Console.WriteLine("聊天室:");
        Console.WriteLine("  join <房间ID> <玩家名称>              - 加入聊天室");
        Console.WriteLine("  say | chat <消息>                     - 发送聊天消息");
        Console.WriteLine();
        Console.WriteLine("匹配与战斗:");
        Console.WriteLine("  match <模式>                          - 开始匹配 (OneVsOne/ThreeVsThree/FiveVsFive)");
        Console.WriteLine("  battle <战斗ID>                       - 加入战斗");
        Console.WriteLine("  ready                                 - 战斗准备");
        Console.WriteLine("  attack <目标角色ID>                   - 攻击目标");
        Console.WriteLine("  skill <技能ID> <目标角色ID>           - 使用技能");
        Console.WriteLine("  leave | leavebattle                   - 离开战斗");
        Console.WriteLine();
        Console.WriteLine("场景模式:");
        Console.WriteLine("  scenario fullbattle <账号> <角色名> <职业> - 运行完整战斗流程场景");
        Console.WriteLine();
        Console.WriteLine("其他:");
        Console.WriteLine("  help | h                              - 显示帮助");
        Console.WriteLine("  exit | quit                           - 退出程序");
        Console.WriteLine();
    }
}
