using System;
using System.Threading.Tasks;
using DistributedGameApp.Shared.Messages;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.Client;

/// <summary>
/// 游戏客户端完整流程示例
/// </summary>
/// <remarks>
/// 此示例展示了完整的游戏客户端连接流程：
/// 1. 连接 LoginServer 进行登录认证
/// 2. 获取并选择游戏服务器（区服）
/// 3. 连接到 GameServer
/// 4. 创建/列表/选择角色
/// 5. 请求匹配
/// 6. 收到匹配通知后自动连接到 BattleServer
/// 7. 加入战斗并准备
/// 8. 战斗结束后离开
/// </remarks>
public class GameClientExample
{
    /// <summary>
    /// 完整流程示例
    /// </summary>
    public static async Task RunFullFlowExampleAsync()
    {
        // 创建日志工厂
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // 创建游戏客户端
        using var client = new GameClient("http://localhost:5000", loggerFactory);

        try
        {
            // ========== 步骤 1: 登录到 LoginServer ==========
            Console.WriteLine("=== 步骤 1: 登录到 LoginServer ===");

            var loginSuccess = await client.LoginAsync("testuser", "password123");
            if (!loginSuccess)
            {
                // 如果登录失败，尝试注册
                Console.WriteLine("登录失败，尝试注册新账号...");
                var registerSuccess = await client.RegisterAsync(
                    "testuser",
                    "password123",
                    "testuser@example.com");

                if (!registerSuccess)
                {
                    Console.WriteLine("注册失败，流程终止");
                    return;
                }
            }

            Console.WriteLine($"已登录: {client.Username} ({client.UserId})");

            // ========== 步骤 2: 获取并选择游戏服务器 ==========
            Console.WriteLine("\n=== 步骤 2: 获取并选择游戏服务器 ===");

            // 方式 1: 获取推荐的服务器
            var recommendedServer = await client.GetRecommendedGameServerAsync();
            if (recommendedServer != null)
            {
                Console.WriteLine($"推荐服务器: {recommendedServer.ServerName}");
                Console.WriteLine($"  负载: {recommendedServer.CurrentPlayers}/{recommendedServer.MaxPlayers} ({recommendedServer.LoadPercentage}%)");
            }

            // 方式 2: 获取所有服务器列表让用户选择
            var servers = await client.GetGameServerListAsync();
            Console.WriteLine($"\n可用的游戏服务器 ({servers.Count}):");
            for (int i = 0; i < servers.Count; i++)
            {
                var server = servers[i];
                Console.WriteLine($"  [{i + 1}] {server.ServerName} - {server.Host}:{server.TcpPort}");
                Console.WriteLine($"      负载: {server.CurrentPlayers}/{server.MaxPlayers} ({server.LoadPercentage}%)");
                Console.WriteLine($"      状态: {server.Status}");
            }

            // 选择第一个服务器（或推荐的服务器）
            var selectedServer = recommendedServer ?? servers.FirstOrDefault();
            if (selectedServer == null)
            {
                Console.WriteLine("没有可用的游戏服务器");
                return;
            }

            // ========== 步骤 3: 连接到 GameServer ==========
            Console.WriteLine($"\n=== 步骤 3: 连接到 GameServer ({selectedServer.ServerName}) ===");

            var connectSuccess = await client.ConnectToGameServerAsync(selectedServer);
            if (!connectSuccess)
            {
                Console.WriteLine("连接到游戏服务器失败");
                return;
            }

            Console.WriteLine("成功连接到游戏服务器");

            // ========== 步骤 4: 角色管理 ==========
            Console.WriteLine("\n=== 步骤 4: 角色管理 ===");

            // 获取角色列表
            var characters = await client.GetCharacterListAsync();
            Console.WriteLine($"已有角色数: {characters.Count}");

            foreach (var character in characters)
            {
                Console.WriteLine($"  - {character.CharacterName} (Lv.{character.Level} {character.Class})");
            }

            CharacterInfo? selectedCharacter = null;

            if (characters.Count == 0)
            {
                // 如果没有角色，创建一个新角色
                Console.WriteLine("\n没有角色，创建新角色...");
                selectedCharacter = await client.CreateCharacterAsync(
                    "MyHero",
                    CharacterClass.Warrior,
                    Gender.Male);

                if (selectedCharacter == null)
                {
                    Console.WriteLine("创建角色失败");
                    return;
                }

                Console.WriteLine($"成功创建角色: {selectedCharacter.CharacterName}");
            }
            else
            {
                // 选择第一个角色
                selectedCharacter = characters[0];
            }

            // 选择角色进入游戏
            var selectSuccess = await client.SelectCharacterAsync(selectedCharacter.CharacterId);
            if (!selectSuccess)
            {
                Console.WriteLine("选择角色失败");
                return;
            }

            Console.WriteLine($"已选择角色: {selectedCharacter.CharacterName}");

            // 显示客户端状态
            client.DisplayStatus();

            // ========== 步骤 5: 请求匹配 ==========
            Console.WriteLine("\n=== 步骤 5: 请求匹配 ===");

            var ticketId = await client.RequestMatchAsync(MatchMode.OneVsOne);
            if (ticketId == null)
            {
                Console.WriteLine("匹配请求失败");
                return;
            }

            Console.WriteLine($"已开始匹配，票据ID: {ticketId}");
            Console.WriteLine("等待匹配结果...");

            // 注意：当匹配成功时，GameEventHandler 会自动连接到 BattleServer
            // 这里我们等待一段时间让匹配完成
            await Task.Delay(TimeSpan.FromSeconds(10));

            // ========== 步骤 6 & 7: 连接到 BattleServer 并加入战斗 ==========
            // 如果匹配成功，GameEventHandler 已经自动连接到 BattleServer
            if (client.IsInBattle)
            {
                Console.WriteLine("\n=== 步骤 6 & 7: 已连接到 BattleServer ===");

                // 加入战斗
                var battleId = client.CurrentBattleServer?.ServerName?.Replace("BattleServer-", "") ?? "unknown";
                var battleInfo = await client.JoinBattleAsync(battleId);
                if (battleInfo != null)
                {
                    Console.WriteLine($"成功加入战斗: {battleInfo.BattleId}");
                    Console.WriteLine($"战斗状态: {battleInfo.Status}");

                    // 准备就绪
                    var readySuccess = await client.BattleReadyAsync();
                    if (readySuccess)
                    {
                        Console.WriteLine("已准备就绪，等待战斗开始...");
                    }

                    // 在这里可以执行战斗操作
                    // 例如：攻击、使用技能等
                    // 这个示例中我们只是等待一段时间

                    await Task.Delay(TimeSpan.FromSeconds(5));

                    // ========== 步骤 8: 离开战斗 ==========
                    Console.WriteLine("\n=== 步骤 8: 离开战斗 ===");

                    var leaveSuccess = await client.LeaveBattleAsync();
                    if (leaveSuccess)
                    {
                        Console.WriteLine("已离开战斗");
                    }
                }
            }
            else
            {
                Console.WriteLine("\n匹配超时或失败，未能进入战斗");

                // 取消匹配
                await client.CancelMatchAsync(ticketId);
            }

            // 最终状态
            Console.WriteLine("\n=== 最终状态 ===");
            client.DisplayStatus();

            Console.WriteLine("\n流程完成！");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n发生错误: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    /// <summary>
    /// 简化的快速开始示例
    /// </summary>
    public static async Task RunQuickStartExampleAsync()
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        using var client = new GameClient("http://localhost:5000", loggerFactory);

        try
        {
            // 1. 登录
            await client.LoginAsync("testuser", "password123");

            // 2. 获取推荐服务器并连接
            var server = await client.GetRecommendedGameServerAsync();
            if (server != null)
            {
                await client.ConnectToGameServerAsync(server);
            }

            // 3. 获取角色列表
            var characters = await client.GetCharacterListAsync();

            // 4. 如果没有角色，创建一个
            if (characters.Count == 0)
            {
                await client.CreateCharacterAsync("MyHero", CharacterClass.Warrior, Gender.Male);
                characters = await client.GetCharacterListAsync();
            }

            // 5. 选择第一个角色
            if (characters.Count > 0)
            {
                await client.SelectCharacterAsync(characters[0].CharacterId);
            }

            // 6. 开始匹配
            await client.RequestMatchAsync(MatchMode.OneVsOne);

            // 显示状态
            client.DisplayStatus();

            Console.WriteLine("客户端准备完成！");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 仅演示登录和获取服务器列表
    /// </summary>
    public static async Task RunLoginAndServerListExampleAsync()
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        using var client = new GameClient("http://localhost:5000", loggerFactory);

        try
        {
            Console.WriteLine("=== 登录示例 ===\n");

            // 登录
            Console.Write("用户名: ");
            var username = Console.ReadLine() ?? "testuser";

            Console.Write("密码: ");
            var password = Console.ReadLine() ?? "password123";

            var loginSuccess = await client.LoginAsync(username, password);

            if (!loginSuccess)
            {
                Console.WriteLine("登录失败，尝试注册...");

                Console.Write("邮箱: ");
                var email = Console.ReadLine() ?? "test@example.com";

                await client.RegisterAsync(username, password, email);
            }

            Console.WriteLine($"\n欢迎, {client.Username}!\n");

            // 获取服务器列表
            Console.WriteLine("=== 服务器列表 ===\n");
            var servers = await client.GetGameServerListAsync();

            foreach (var server in servers)
            {
                Console.WriteLine($"服务器: {server.ServerName}");
                Console.WriteLine($"  地址: {server.Host}:{server.TcpPort}");
                Console.WriteLine($"  负载: {server.CurrentPlayers}/{server.MaxPlayers} ({server.LoadPercentage}%)");
                Console.WriteLine($"  状态: {server.Status}");
                Console.WriteLine();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"错误: {ex.Message}");
        }
    }
}
