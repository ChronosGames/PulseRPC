using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Client;
using PulseRPC.Client.Platform.Net;
using Xunit;

namespace PulseRPC.Client.Tests
{
    /// <summary>
    /// 平台适配器单元测试
    /// </summary>
    public class PlatformAdapterTests
    {
        [Fact]
        public void PlatformAdapterFactory_CreateAdapter_ShouldReturnNetAdapterInNonUnityEnvironment()
        {
            // Arrange & Act
            var adapter = PlatformAdapterFactory.CreateAdapter();

            // Assert
            Assert.NotNull(adapter);
            Assert.IsType<NetPlatformAdapter>(adapter);
        }

        [Fact]
        public void PlatformAdapterFactory_IsUnityPlatform_ShouldReturnFalseInNonUnityEnvironment()
        {
            // Arrange & Act
            var isUnity = PlatformAdapterFactory.IsUnityPlatform();

            // Assert
            Assert.False(isUnity);
        }

        [Fact]
        public void NetPlatformAdapter_CreateLogger_ShouldReturnLogger()
        {
            // Arrange
            var loggerFactory = NullLoggerFactory.Instance;
            var adapter = new NetPlatformAdapter(loggerFactory);

            // Act
            var logger = adapter.CreateLogger<PlatformAdapterTests>();

            // Assert
            Assert.NotNull(logger);
        }

        [Fact]
        public async Task NetPlatformAdapter_Delay_ShouldDelayExecution()
        {
            // Arrange
            var adapter = new NetPlatformAdapter();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Act
            await adapter.Delay(100);

            // Assert
            stopwatch.Stop();
            Assert.True(stopwatch.ElapsedMilliseconds >= 80); // 允许一些时间误差
        }

        [Fact]
        public void NetPlatformAdapter_ConfigureThreading_ShouldNotThrow()
        {
            // Arrange
            var adapter = new NetPlatformAdapter();

            // Act & Assert
            adapter.ConfigureThreading(); // 应该不会抛出异常
        }

        [Fact]
        public void NetPlatformAdapter_IsMainThread_ShouldReturnTrueOnCreationThread()
        {
            // Arrange
            var adapter = new NetPlatformAdapter();

            // Act
            var isMainThread = adapter.IsMainThread();

            // Assert
            Assert.True(isMainThread);
        }

        [Fact]
        public void NetPlatformAdapter_InvokeOnMainThread_ShouldExecuteAction()
        {
            // Arrange
            var adapter = new NetPlatformAdapter();
            var executed = false;
            var action = new Action(() => executed = true);

            // Act
            adapter.InvokeOnMainThread(action);

            // Assert
            Assert.True(executed);
        }

        [Fact]
        public async Task NetPlatformAdapter_InvokeOnMainThread_ShouldExecuteActionFromBackgroundThread()
        {
            // Arrange
            var adapter = new NetPlatformAdapter();
            var executed = false;
            var action = new Action(() => executed = true);

            // Act
            await Task.Run(() => adapter.InvokeOnMainThread(action));

            // 等待一小段时间确保任务执行
            await Task.Delay(100);

            // Assert
            Assert.True(executed);
        }
    }
}
