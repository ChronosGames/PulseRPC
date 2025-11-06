using System;
using System.Collections.Generic;
using MemoryPack;

namespace DistributedGameApp.Shared.Domain.Characters;

/// <summary>
/// 创建角色请求
/// </summary>
[MemoryPackable]
public partial class CreateCharacterRequest
{
    /// <summary>
    /// 角色名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 职业
    /// </summary>
    public string Class { get; set; } = string.Empty;
}

/// <summary>
/// 创建角色响应
/// </summary>
[MemoryPackable]
public partial class CreateCharacterResponse
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 错误消息
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// 创建的角色
    /// </summary>
    public Character? Character { get; set; }
}

/// <summary>
/// 获取角色列表响应
/// </summary>
[MemoryPackable]
public partial class GetCharactersResponse
{
    /// <summary>
    /// 角色列表
    /// </summary>
    public List<CharacterSummary> Characters { get; set; } = new();
}

/// <summary>
/// 角色摘要信息
/// </summary>
[MemoryPackable]
public partial class CharacterSummary
{
    /// <summary>
    /// 角色ID
    /// </summary>
    public string CharacterId { get; set; } = string.Empty;

    /// <summary>
    /// 角色名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 职业
    /// </summary>
    public string Class { get; set; } = string.Empty;

    /// <summary>
    /// 等级
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// 最后上线时间
    /// </summary>
    public DateTime LastOnlineAt { get; set; }
}
