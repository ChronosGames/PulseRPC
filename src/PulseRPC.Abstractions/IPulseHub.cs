namespace PulseRPC;

/// <summary>
/// PulseRPC 远程服务基础接口 - 标记接口可被远程调用
/// 这是三层抽象架构中的服务标准化接口，替代原有的IPulseService
/// </summary>
public interface IPulseHub
{
    // 所有远程服务都应继承此接口
}

/// <summary>
/// 向后兼容的服务接口别名
/// 为了保持与现有代码的兼容性，IPulseService现在继承IPulseHub
/// </summary>
[Obsolete("IPulseService is deprecated. Please use IPulseHub instead.", false)]
public interface IPulseService : IPulseHub
{
    // 向后兼容，现有代码可以继续使用IPulseService
    // 但新代码建议使用IPulseHub
}
