using MemoryPack;

namespace MiniGame.Client;

// 定义请求模型
[MemoryPackable]
public partial class TestRequest
{
    public string Id { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

// 定义响应模型
[MemoryPackable]
public partial class TestResponse
{
    public bool Success { get; set; }
    public string Result { get; set; } = string.Empty;
} 