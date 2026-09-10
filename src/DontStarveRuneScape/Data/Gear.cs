namespace DontStarveRuneScape.Data;

using System.Text.Json.Serialization;
using DontStarveRuneScape.Core;

/// <summary>
/// Gear item definition (weapons/armor).
/// </summary>
public sealed class GearDef : DataRecord
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty; // "weapon" or "armor"

    [JsonPropertyName("slot")]
    public string Slot { get; init; } = string.Empty; // "weapon", "head", "chest", "legs", "boots", "gloves", "cape", "ammo", "shield"

    [JsonPropertyName("damage")]
    public float Damage { get; init; }

    [JsonPropertyName("attack_bonus")]
    public float AttackBonus { get; init; }

    [JsonPropertyName("defence_bonus")]
    public float DefenceBonus { get; init; }

    [JsonPropertyName("speed_bonus")]
    public float SpeedBonus { get; init; }

    [JsonPropertyName("required_level")]
    public int RequiredLevel { get; init; } = 1;

    [JsonPropertyName("tier")]
    public int Tier { get; init; } = 1;

    [JsonPropertyName("sprite_key")]
    public string SpriteKey { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Create a GearItem from this definition.
    /// </summary>
    public GearItem CreateItem()
    {
        return new GearItem
        {
            Id = Id,
            Name = Name,
            Type = Type,
            Slot = Slot,
            Damage = Damage,
            AttackBonus = AttackBonus,
            DefenceBonus = DefenceBonus,
            SpeedBonus = SpeedBonus,
            RequiredLevel = RequiredLevel,
            Tier = Tier,
            SpriteKey = SpriteKey,
            Description = Description,
        };
    }
}

/// <summary>
/// Runtime gear item instance.
/// </summary>
public sealed class GearItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Slot { get; set; } = string.Empty;
    public float Damage { get; set; }
    public float AttackBonus { get; set; }
    public float DefenceBonus { get; set; }
    public float SpeedBonus { get; set; }
    public int RequiredLevel { get; set; }
    public int Tier { get; set; }
    public string SpriteKey { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public static Dictionary<string, GearItem> LoadAll()
    {
        // This will be populated from GearDef registry at runtime
        return [];
    }
}

/// <summary>
/// Player gear equipment slots.
/// </summary>
public sealed class PlayerGear
{
    public GearItem? Weapon { get; set; }
    public GearItem? Head { get; set; }
    public GearItem? Chest { get; set; }
    public GearItem? Legs { get; set; }
    public GearItem? Boots { get; set; }
    public GearItem? Gloves { get; set; }
    public GearItem? Cape { get; set; }
    public GearItem? Ammo { get; set; }
    public GearItem? Shield { get; set; }

    public void Equip(GearItem item)
    {
        switch (item.Slot.ToLowerInvariant())
        {
            case "weapon": Weapon = item; break;
            case "head": Head = item; break;
            case "chest": Chest = item; break;
            case "legs": Legs = item; break;
            case "boots": Boots = item; break;
            case "gloves": Gloves = item; break;
            case "cape": Cape = item; break;
            case "ammo": Ammo = item; break;
            case "shield": Shield = item; break;
        }
    }

    public void Unequip(string slot)
    {
        switch (slot.ToLowerInvariant())
        {
            case "weapon": Weapon = null; break;
            case "head": Head = null; break;
            case "chest": Chest = null; break;
            case "legs": Legs = null; break;
            case "boots": Boots = null; break;
            case "gloves": Gloves = null; break;
            case "cape": Cape = null; break;
            case "ammo": Ammo = null; break;
            case "shield": Shield = null; break;
        }
    }

    public GearItem? GetEquipped(string slot)
    {
        return slot.ToLowerInvariant() switch
        {
            "weapon" => Weapon,
            "head" => Head,
            "chest" => Chest,
            "legs" => Legs,
            "boots" => Boots,
            "gloves" => Gloves,
            "cape" => Cape,
            "ammo" => Ammo,
            "shield" => Shield,
            _ => null,
        };
    }

    public float GetTotalAttackBonus()
    {
        float total = 0;
        if (Weapon != null) total += Weapon.AttackBonus;
        return total;
    }

    public float GetTotalDefenceBonus()
    {
        float total = 0;
        if (Head != null) total += Head.DefenceBonus;
        if (Chest != null) total += Chest.DefenceBonus;
        if (Legs != null) total += Legs.DefenceBonus;
        if (Boots != null) total += Boots.DefenceBonus;
        if (Gloves != null) total += Gloves.DefenceBonus;
        if (Cape != null) total += Cape.DefenceBonus;
        if (Shield != null) total += Shield.DefenceBonus;
        return total;
    }

    public float GetTotalSpeedBonus()
    {
        float total = 0;
        if (Weapon != null) total += Weapon.SpeedBonus;
        if (Head != null) total += Head.SpeedBonus;
        if (Chest != null) total += Chest.SpeedBonus;
        if (Legs != null) total += Legs.SpeedBonus;
        if (Boots != null) total += Boots.SpeedBonus;
        if (Gloves != null) total += Gloves.SpeedBonus;
        if (Cape != null) total += Cape.SpeedBonus;
        if (Shield != null) total += Shield.SpeedBonus;
        return total;
    }

    public float GetWeaponDamage()
    {
        return Weapon?.Damage ?? 1.0f; // Unarmed = 1 damage
    }

    /// <summary>Get snapshot for saving.</summary>
    public GearSnapshot GetSnapshot()
    {
        return new GearSnapshot
        {
            Weapon = Weapon?.Id,
            Head = Head?.Id,
            Chest = Chest?.Id,
            Legs = Legs?.Id,
            Boots = Boots?.Id,
            Gloves = Gloves?.Id,
            Cape = Cape?.Id,
            Ammo = Ammo?.Id,
            Shield = Shield?.Id,
        };
    }

    /// <summary>Restore from snapshot.</summary>
    public void RestoreSnapshot(GearSnapshot snapshot, Dictionary<string, GearItem> gearRegistry)
    {
        if (!string.IsNullOrEmpty(snapshot.Weapon) && gearRegistry.TryGetValue(snapshot.Weapon, out var w)) Weapon = w;
        if (!string.IsNullOrEmpty(snapshot.Head) && gearRegistry.TryGetValue(snapshot.Head, out var h)) Head = h;
        if (!string.IsNullOrEmpty(snapshot.Chest) && gearRegistry.TryGetValue(snapshot.Chest, out var c)) Chest = c;
        if (!string.IsNullOrEmpty(snapshot.Legs) && gearRegistry.TryGetValue(snapshot.Legs, out var l)) Legs = l;
        if (!string.IsNullOrEmpty(snapshot.Boots) && gearRegistry.TryGetValue(snapshot.Boots, out var b)) Boots = b;
        if (!string.IsNullOrEmpty(snapshot.Gloves) && gearRegistry.TryGetValue(snapshot.Gloves, out var g)) Gloves = g;
        if (!string.IsNullOrEmpty(snapshot.Cape) && gearRegistry.TryGetValue(snapshot.Cape, out var ca)) Cape = ca;
        if (!string.IsNullOrEmpty(snapshot.Ammo) && gearRegistry.TryGetValue(snapshot.Ammo, out var a)) Ammo = a;
        if (!string.IsNullOrEmpty(snapshot.Shield) && gearRegistry.TryGetValue(snapshot.Shield, out var s)) Shield = s;
    }
}