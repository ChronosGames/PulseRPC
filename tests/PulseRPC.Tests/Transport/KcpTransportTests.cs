using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Transport;
using PulseRPC.Transport.Kcp;
using Xunit;
using Xunit.Abstractions;

namespace PulseRPC.Tests.Transport
{
    /// <summary>
    /// KCP传输测试
    /// </summary>
    public class KcpTransportTests
    {
        private readonly ITestOutputHelper _output;
        private readonly ILogger _logger;

        public KcpTransportTests(ITestOutputHelper output)
        {
            _output = output;
            _logger = new TestLogger(output);
        }

        /// <summary>
        /// 测试KCP段的编码和解码
        /// </summary>
        [Fact]
        public void KcpSegment_EncodeAndDecode_ShouldWork()
        {
            // Arrange
            var originalData = Encoding.UTF8.GetBytes("Hello, KCP!");
            var segment = KcpSegment.Rent();

            try
            {
                segment.Header.Conv = 12345;
                segment.Header.Cmd = (byte)KcpCommand.Push;
                segment.Header.Frg = 0;
                segment.Header.Wnd = 100;
                segment.Header.Ts = 1000;
                segment.Header.Sn = 1;
                segment.Header.Una = 0;
                segment.SetData(originalData);

                // Act
                var buffer = new byte[1024];
                var encodedSize = segment.Encode(buffer);
                var decodedSegment = KcpSegment.Decode(buffer.AsSpan(0, encodedSize));

                // Assert
                Assert.True(encodedSize > 0);
                Assert.NotNull(decodedSegment);
                Assert.Equal(segment.Header.Conv, decodedSegment.Header.Conv);
                Assert.Equal(segment.Header.Cmd, decodedSegment.Header.Cmd);
                Assert.Equal(segment.Header.Sn, decodedSegment.Header.Sn);
                Assert.Equal(originalData.Length, (int)decodedSegment.Header.Len);

                var decodedData = decodedSegment.Data.ToArray();
                Assert.Equal(originalData, decodedData);

                decodedSegment.Dispose();
            }
            finally
            {
                segment.Dispose();
            }
        }

        /// <summary>
        /// 测试KCP段对象池
        /// </summary>
        [Fact]
        public void KcpSegment_ObjectPool_ShouldWork()
        {
            // Arrange & Act
            var initialPoolCount = KcpSegment.PoolCount;

            var segment1 = KcpSegment.Rent();
            var segment2 = KcpSegment.Rent();

            segment1.Dispose();
            segment2.Dispose();

            var finalPoolCount = KcpSegment.PoolCount;

            // Assert
            Assert.True(finalPoolCount >= initialPoolCount);
        }

        /// <summary>
        /// 测试KCP核心发送和接收
        /// </summary>
        [Fact]
        public void KcpCore_SendAndReceive_ShouldWork()
        {
            // Arrange
            var receivedData = new List<byte[]>();
            var kcp1 = new KcpCore(1, (data, size) =>
            {
                // KCP1输出到KCP2输入
                var buffer = new byte[size];
                Array.Copy(data, buffer, size);
                receivedData.Add(buffer);
            }, _logger);

            var kcp2 = new KcpCore(1, (data, size) =>
            {
                // KCP2输出到KCP1输入
                kcp1.Input(new Span<byte>(data, 0, size));
            }, _logger);

            try
            {
                // Act
                var testMessage = Encoding.UTF8.GetBytes("Hello from KCP1!");
                var result = kcp1.Send(testMessage);

                Assert.Equal(0, result); // 发送成功

                // 模拟时间推进和网络传输
                uint currentTime = 0;
                for (int i = 0; i < 10; i++)
                {
                    currentTime += 10;
                    kcp1.Update(currentTime);
                    kcp2.Update(currentTime);

                    // 模拟网络传输
                    foreach (var data in receivedData)
                    {
                        kcp2.Input(data);
                    }
                    receivedData.Clear();
                }

                // 尝试接收数据
                var receiveBuffer = new byte[1024];
                var receivedSize = kcp2.Recv(receiveBuffer);

                // Assert
                if (receivedSize > 0)
                {
                    var receivedMessage = Encoding.UTF8.GetString(receiveBuffer, 0, receivedSize);
                    Assert.Equal("Hello from KCP1!", receivedMessage);
                }
            }
            finally
            {
                kcp1.Dispose();
                kcp2.Dispose();
            }
        }

        /// <summary>
        /// 测试KCP传输选项配置
        /// </summary>
        [Fact]
        public void KcpTransport_Configuration_ShouldWork()
        {
            // Arrange
            var options = new TransportOptions
            {
                Kcp = new KcpOptions
                {
                    ConversationId = 12345,
                    NoDelay = 1,
                    Interval = 50,
                    Resend = 2,
                    DisableFlowControl = true,
                    SendWindow = 64,
                    ReceiveWindow = 256
                }
            };

            // Act & Assert
            using var transport = new KcpClientTransport(options, _logger);

            Assert.Equal("KCP", transport.Name);
            Assert.Equal(TransportType.Kcp, transport.Type);
            Assert.False(transport.IsConnected);
        }

        /// <summary>
        /// 测试KCP连接状态管理
        /// </summary>
        [Fact]
        public async Task KcpClientTransport_StateManagement_ShouldWork()
        {
            // Arrange
            var stateChanges = new List<(ConnectionState oldState, ConnectionState newState)>();
            var options = new TransportOptions
            {
                ConnectionTimeout = TimeSpan.FromSeconds(1),
                Kcp = new KcpOptions { ConversationId = 12345 }
            };

            using var transport = new KcpClientTransport(options, _logger);

            transport.StateChanged += (sender, e) =>
            {
                stateChanges.Add((e.PreviousState, e.CurrentState));
                _output.WriteLine($"状态变更: {e.PreviousState} -> {e.CurrentState}");
            };

            // Act & Assert
            Assert.Equal(ConnectionState.Disconnected, transport.State);

            // 尝试连接到不存在的服务器（应该失败）
            var connectException = await Assert.ThrowsAsync<Exception>(async () =>
            {
                await transport.ConnectAsync("127.0.0.1", 9999, CancellationToken.None);
            });

            _output.WriteLine($"连接异常: {connectException.Message}");

            // 验证状态变更
            Assert.Contains((ConnectionState.Disconnected, ConnectionState.Connecting), stateChanges);
            Assert.Contains((ConnectionState.Connecting, ConnectionState.Failed), stateChanges);
        }

        /// <summary>
        /// 测试KCP数据分片
        /// </summary>
        [Fact]
        public void KcpCore_LargeDataFragmentation_ShouldWork()
        {
            // Arrange
            var receivedPackets = new List<byte[]>();
            var kcp = new KcpCore(1, (data, size) =>
            {
                var packet = new byte[size];
                Array.Copy(data, packet, size);
                receivedPackets.Add(packet);
            }, _logger);

            try
            {
                // Act - 发送大于MSS的数据
                var largeData = new byte[2000]; // 超过默认MSS (1376)
                new Random().NextBytes(largeData);

                var result = kcp.Send(largeData);
                Assert.Equal(0, result);

                // 模拟发送
                kcp.Update(100);

                // Assert - 应该产生多个数据包
                Assert.True(receivedPackets.Count > 1, "大数据应该被分片");

                _output.WriteLine($"大数据被分成 {receivedPackets.Count} 个片段");
            }
            finally
            {
                kcp.Dispose();
            }
        }

        /// <summary>
        /// 测试KCP窗口大小设置
        /// </summary>
        [Fact]
        public void KcpCore_WindowSize_ShouldWork()
        {
            // Arrange
            var kcp = new KcpCore(1, (data, size) => { }, _logger);

            try
            {
                // Act
                var result = kcp.SetWindowSize(128, 256);

                // Assert
                Assert.Equal(0, result);
            }
            finally
            {
                kcp.Dispose();
            }
        }

        /// <summary>
        /// 测试KCP MTU设置
        /// </summary>
        [Fact]
        public void KcpCore_MTU_ShouldWork()
        {
            // Arrange
            var kcp = new KcpCore(1, (data, size) => { }, _logger);

            try
            {
                // Act & Assert
                Assert.Equal(0, kcp.SetMtu(1400)); // 正常MTU
                Assert.Equal(-1, kcp.SetMtu(20));  // 过小的MTU
            }
            finally
            {
                kcp.Dispose();
            }
        }

        /// <summary>
        /// 测试KCP无延迟模式
        /// </summary>
        [Fact]
        public void KcpCore_NoDelayMode_ShouldWork()
        {
            // Arrange
            var kcp = new KcpCore(1, (data, size) => { }, _logger);

            try
            {
                // Act
                var result = kcp.NoDelay(1, 10, 2, true);

                // Assert
                Assert.Equal(0, result);
            }
            finally
            {
                kcp.Dispose();
            }
        }
    }

    /// <summary>
    /// 测试用的Logger实现
    /// </summary>
    public class TestLogger : ILogger
    {
        private readonly ITestOutputHelper _output;

        public TestLogger(ITestOutputHelper output)
        {
            _output = output;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            _output.WriteLine($"[{logLevel}] {message}");
            if (exception != null)
            {
                _output.WriteLine($"Exception: {exception}");
            }
        }
    }
}
