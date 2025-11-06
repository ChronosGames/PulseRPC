using System;
using System.Collections.Generic;
using MemoryPack;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DistributedGameApp.Shared.Domain.Characters;

/// <summary>
/// 游戏角色
/// </summary>
[MemoryPackable]
public partial class Character
{
    /// <summary>
    /// MongoDB 主键
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 角色ID
    /// </summary>
    [BsonElement("characterId")]
    public string CharacterId { get; set; } = string.Empty;

    /// <summary>
    /// 所属用户ID
    /// </summary>
    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// 角色名称
    /// </summary>
    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 角色职业
    /// </summary>
    [BsonElement("class")]
    public string Class { get; set; } = string.Empty;

    /// <summary>
    /// 等级
    /// </summary>
    [BsonElement("level")]
    public int Level { get; set; } = 1;

    /// <summary>
    /// 经验值
    /// </summary>
    [BsonElement("exp")]
    public long Exp { get; set; } = 0;

    /// <summary>
    /// 金币
    /// </summary>
    [BsonElement("gold")]
    public long Gold { get; set; } = 0;

    /// <summary>
    /// 钻石
    /// </summary>
    [BsonElement("diamond")]
    public int Diamond { get; set; } = 0;

    /// <summary>
    /// 属性
    /// </summary>
    [BsonElement("attributes")]
    public CharacterAttributes Attributes { get; set; } = new();

    /// <summary>
    /// 背包
    /// </summary>
    [BsonElement("inventory")]
    public Inventory Inventory { get; set; } = new();

    /// <summary>
    /// 装备
    /// </summary>
    [BsonElement("equipment")]
    public Equipment Equipment { get; set; } = new();

    /// <summary>
    /// 创建时间
    /// </summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后上线时间
    /// </summary>
    [BsonElement("lastOnlineAt")]
    public DateTime LastOnlineAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 角色属性
/// </summary>
[MemoryPackable]
public partial class CharacterAttributes
{
    /// <summary>
    /// 生命值
    /// </summary>
    public int Hp { get; set; } = 100;

    /// <summary>
    /// 最大生命值
    /// </summary>
    public int MaxHp { get; set; } = 100;

    /// <summary>
    /// 魔法值
    /// </summary>
    public int Mp { get; set; } = 50;

    /// <summary>
    /// 最大魔法值
    /// </summary>
    public int MaxMp { get; set; } = 50;

    /// <summary>
    /// 攻击力
    /// </summary>
    public int Attack { get; set; } = 10;

    /// <summary>
    /// 防御力
    /// </summary>
    public int Defense { get; set; } = 5;

    /// <summary>
    /// 速度
    /// </summary>
    public int Speed { get; set; } = 10;
}

/// <summary>
/// 背包
/// </summary>
[MemoryPackable]
public partial class Inventory
{
    /// <summary>
    /// 背包容量
    /// </summary>
    public int Capacity { get; set; } = 100;

    /// <summary>
    /// 物品列表
    /// </summary>
    public List<InventoryItem> Items { get; set; } = new();
}

/// <summary>
/// 背包物品
/// </summary>
[MemoryPackable]
public partial class InventoryItem
{
    /// <summary>
    /// 物品ID
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// 物品模板ID
    /// </summary>
    public int TemplateId { get; set; }

    /// <summary>
    /// 物品名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 物品类型
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 数量
    /// </summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// 品质（Common, Uncommon, Rare, Epic, Legendary）
    /// </summary>
    public string Quality { get; set; } = "Common";
}

/// <summary>
/// 装备
/// </summary>
[MemoryPackable]
public partial class Equipment
{
    /// <summary>
    /// 武器
    /// </summary>
    public EquipmentSlot? Weapon { get; set; }

    /// <summary>
    /// 头盔
    /// </summary>
    public EquipmentSlot? Helmet { get; set; }

    /// <summary>
    /// 护甲
    /// </summary>
    public EquipmentSlot? Armor { get; set; }

    /// <summary>
    /// 裤子
    /// </summary>
    public EquipmentSlot? Pants { get; set; }

    /// <summary>
    /// 鞋子
    /// </summary>
    public EquipmentSlot? Boots { get; set; }

    /// <summary>
    /// 饰品1
    /// </summary>
    public EquipmentSlot? Accessory1 { get; set; }

    /// <summary>
    /// 饰品2
    /// </summary>
    public EquipmentSlot? Accessory2 { get; set; }
}

/// <summary>
/// 装备槽
/// </summary>
[MemoryPackable]
public partial class EquipmentSlot
{
    /// <summary>
    /// 物品ID
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// 物品模板ID
    /// </summary>
    public int TemplateId { get; set; }

    /// <summary>
    /// 物品名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 强化等级
    /// </summary>
    public int EnhanceLevel { get; set; } = 0;

    /// <summary>
    /// 属性加成
    /// </summary>
    public Dictionary<string, int> AttributeBonus { get; set; } = new();
}
