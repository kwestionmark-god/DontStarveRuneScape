namespace DontStarveRuneScape.Core;

using System.Text.Json;
using System.IO;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.World;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Inventory;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Seasons;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Config;

/// <summary>
/// Save/load system for game state.
/// </summary>
public sealed class SaveSystem
{
    public int SlotCount => Constants.SaveSlotCount;

    private readonly string _saveDirectory;

    public SaveSystem()
    {
        _saveDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DontStarveRuneScape", "saves");
        Directory.CreateDirectory(_saveDirectory);
    }

    /// <summary>
    /// Check if a save slot has data.
    /// </summary>
    public bool HasSave(int slot)
    {
        string path = GetSlotPath(slot);
        return File.Exists(path);
    }

    /// <summary>
    /// Save game to slot.
    /// </summary>
    public void Save(Game game, int slot = 0)
    {
        var saveData = BuildSnapshot(game);
        string json = JsonSerializer.Serialize(saveData, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(GetSlotPath(slot), json);
    }

    /// <summary>
    /// Load save from slot.
    /// </summary>
    public SaveData? Load(int slot)
    {
        string path = GetSlotPath(slot);
        if (!File.Exists(path)) return null;

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SaveData>(json);
    }

    /// <summary>
    /// Load save data into game instance.
    /// </summary>
    public void LoadIntoGame(SaveData saveData, Game game)
    {
        // Restore in order: skills -> survival -> inventory -> gear -> position -> structures -> fires -> NPCs -> quests -> factions
        RestoreSkills(saveData, game);
        RestoreSurvival(saveData, game);
        RestoreInventory(saveData, game);
        RestoreGear(saveData, game);
        RestorePosition(saveData, game);
        RestoreStructures(saveData, game);
        RestoreFires(saveData, game);
        RestoreNPCs(saveData, game);
        RestoreQuests(saveData, game);
        RestoreFactions(saveData, game);
        RestoreWorld(saveData, game);
        RestoreSeasons(saveData, game);
    }

    /// <summary>
    /// List all save slots with metadata.
    /// </summary>
    public List<SaveSlotInfo> ListSlots()
    {
        var slots = new List<SaveSlotInfo>();
        for (int i = 0; i < SlotCount; i++)
        {
            string path = GetSlotPath(i);
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                var data = Load(i);
                slots.Add(new SaveSlotInfo
                {
                    Slot = i,
                    LastModified = info.LastWriteTime,
                    Seed = data?.Seed ?? 0,
                    PlayTime = data?.PlayTime ?? 0,
                    DeathCount = data?.DeathCount ?? 0,
                });
            }
            else
            {
                slots.Add(new SaveSlotInfo { Slot = i, LastModified = DateTime.MinValue });
            }
        }
        return slots;
    }

    private string GetSlotPath(int slot) => Path.Combine(_saveDirectory, $"slot_{slot}.json");

    private SaveData BuildSnapshot(Game game)
    {
        return new SaveData
        {
            Version = 1,
            Seed = game.Seed,
            DeathCount = game.DeathCount,
            PlayTime = game.PlayTime,
            Timestamp = DateTime.UtcNow,
            Skills = game.SkillManager?.GetSnapshot() ?? new SkillSnapshot(),
            Survival = game.Survival?.GetSnapshot() ?? new SurvivalSnapshot(),
            Inventory = game.Inventory?.GetSnapshot() ?? new InventorySnapshot(),
            Gear = game.Player?.Gear?.GetSnapshot() ?? new GearSnapshot(),
            Position = game.Player != null ? new PositionSnapshot
            {
                X = game.Player.WorldX,
                Y = game.Player.WorldY,
            } : new PositionSnapshot(),
            Structures = game.BuildingSystem?.GetSnapshot() ?? new BuildingSnapshot(),
            Fires = game.Firemaking?.GetSnapshot() ?? new FireSnapshot(),
            NPCs = game.NPCSystem?.GetSnapshot() ?? new NPCSnapshot(),
            Quests = game.QuestSystem?.GetSnapshot() ?? new QuestSnapshot(),
            Factions = game.FactionSystem?.GetSnapshot() ?? new FactionSnapshot(),
            World = new WorldSnapshot
            {
                DepletedNodes = game.World?.GetDepletedNodes() ?? [],
            },
            Seasons = game.SeasonSystem?.GetSnapshot() ?? new SeasonSnapshot(),
        };
    }

    // Restore methods - each restores a specific subsystem
    private void RestoreSkills(SaveData data, Game game)
    {
        game.SkillManager?.RestoreSnapshot(data.Skills);
    }

    private void RestoreSurvival(SaveData data, Game game)
    {
        game.Survival?.RestoreSnapshot(data.Survival);
    }

    private void RestoreInventory(SaveData data, Game game)
    {
        game.Inventory?.RestoreSnapshot(data.Inventory);
    }

    private void RestoreGear(SaveData data, Game game)
    {
        if (game.DataLoader != null)
        {
            var gearRegistry = GearItem.LoadAll();
            // Merge with data loader if needed
            foreach (var item in game.DataLoader.GearData)
            {
                if (item.TryGetValue("id", out var id) && id is string idStr && !gearRegistry.ContainsKey(idStr))
                {
                    // Could deserialize GearItem from dict, but for now just use LoadAll
                }
            }
            game.Player?.Gear?.RestoreSnapshot(data.Gear, gearRegistry);
        }
    }

    private void RestorePosition(SaveData data, Game game)
    {
        if (game.Player != null)
        {
            game.Player.WorldX = data.Position.X;
            game.Player.WorldY = data.Position.Y;
            game.Player.TargetX = data.Position.X;
            game.Player.TargetY = data.Position.Y;
            game.Player.Moving = false;
        }
    }

    private void RestoreStructures(SaveData data, Game game)
    {
        if (game.DataLoader != null)
        {
            game.BuildingSystem?.RestoreSnapshot(data.Structures, game.World!, game.DataLoader);
        }
    }

    private void RestoreFires(SaveData data, Game game)
    {
        game.Firemaking?.RestoreSnapshot(data.Fires);
    }

    private void RestoreNPCs(SaveData data, Game game)
    {
        if (game.DataLoader != null)
        {
            game.NPCSystem?.RestoreSnapshot(data.NPCs, game.DataLoader);
        }
    }

    private void RestoreQuests(SaveData data, Game game)
    {
        if (game.DataLoader != null)
        {
            game.QuestSystem?.RestoreSnapshot(data.Quests, game.DataLoader);
        }
    }

    private void RestoreFactions(SaveData data, Game game)
    {
        if (game.DataLoader != null)
        {
            game.FactionSystem?.RestoreSnapshot(data.Factions, game.DataLoader);
        }
    }

    private void RestoreWorld(SaveData data, Game game)
    {
        game.World?.RestoreDepletedNodes(data.World.DepletedNodes);
    }

    private void RestoreSeasons(SaveData data, Game game)
    {
        game.SeasonSystem?.RestoreSnapshot(data.Seasons);
    }
}

/// <summary>
/// Complete save data structure.
/// </summary>
public sealed class SaveData
{
    public int Version { get; set; }
    public int Seed { get; set; }
    public int DeathCount { get; set; }
    public float PlayTime { get; set; }
    public DateTime Timestamp { get; set; }

    public SkillSnapshot Skills { get; set; } = new();
    public SurvivalSnapshot Survival { get; set; } = new();
    public InventorySnapshot Inventory { get; set; } = new();
    public GearSnapshot Gear { get; set; } = new();
    public PositionSnapshot Position { get; set; } = new();
    public BuildingSnapshot Structures { get; set; } = new();
    public FireSnapshot Fires { get; set; } = new();
    public NPCSnapshot NPCs { get; set; } = new();
    public QuestSnapshot Quests { get; set; } = new();
    public FactionSnapshot Factions { get; set; } = new();
    public WorldSnapshot World { get; set; } = new();
    public SeasonSnapshot Seasons { get; set; } = new();
}

/// <summary>
/// Save slot metadata.
/// </summary>
public sealed class SaveSlotInfo
{
    public int Slot { get; set; }
    public DateTime LastModified { get; set; }
    public int Seed { get; set; }
    public float PlayTime { get; set; }
    public int DeathCount { get; set; }
    public bool HasData => LastModified != DateTime.MinValue;
}

// Snapshot classes - each subsystem implements these
public sealed class SkillSnapshot
{
    public Dictionary<string, SkillDataSnapshot> Skills { get; set; } = [];
}

public sealed class SkillDataSnapshot
{
    public int Level { get; set; }
    public float Xp { get; set; }
    public int StatPoints { get; set; }
    public Dictionary<string, int> SubStats { get; set; } = [];
}

public sealed class SurvivalSnapshot
{
    public float Hp { get; set; }
    public float MaxHp { get; set; }
    public float Hunger { get; set; }
    public float MaxHunger { get; set; }
    public float Stamina { get; set; }
    public float MaxStamina { get; set; }
}

public sealed class InventorySnapshot
{
    public InventorySlotSnapshot[] Slots { get; set; } = [];
}

public sealed class InventorySlotSnapshot
{
    public string ItemId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public float SpoilageRemaining { get; set; }
    public bool IsEquipped { get; set; }
}

public sealed class GearSnapshot
{
    public string? Weapon { get; set; }
    public string? Head { get; set; }
    public string? Chest { get; set; }
    public string? Legs { get; set; }
    public string? Boots { get; set; }
    public string? Gloves { get; set; }
    public string? Cape { get; set; }
    public string? Ammo { get; set; }
    public string? Shield { get; set; }
}

public sealed class PositionSnapshot
{
    public float X { get; set; }
    public float Y { get; set; }
}

public sealed class BuildingSnapshot
{
    public StructureSnapshot[] Structures { get; set; } = [];
}

public sealed class StructureSnapshot
{
    public string StructureId { get; set; } = string.Empty;
    public int TileX { get; set; }
    public int TileY { get; set; }
    public float Hp { get; set; }
    public string? AssignedNpcId { get; set; }
}

public sealed class FireSnapshot
{
    public FireDataSnapshot[] Fires { get; set; } = [];
}

public sealed class FireDataSnapshot
{
    public float WorldX { get; set; }
    public float WorldY { get; set; }
    public float RemainingTime { get; set; }
    public float MaxTime { get; set; }
}

public sealed class NPCSnapshot
{
    public NPCDataSnapshot[] NPCs { get; set; } = [];
}

public sealed class NPCDataSnapshot
{
    public string NpcId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public float WorldX { get; set; }
    public float WorldY { get; set; }
    public float Health { get; set; }
    public bool IsActive { get; set; }
    public string? RecruitedBy { get; set; }
}

public sealed class QuestSnapshot
{
    public QuestDataSnapshot[] Quests { get; set; } = [];
}

public sealed class QuestDataSnapshot
{
    public string QuestId { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty; // "available", "active", "completed", "failed"
    public Dictionary<string, int> Progress { get; set; } = [];
}

public sealed class FactionSnapshot
{
    public FactionDataSnapshot[] Factions { get; set; } = [];
}

public sealed class FactionDataSnapshot
{
    public string FactionId { get; set; } = string.Empty;
    public int Standing { get; set; }
}

public sealed class WorldSnapshot
{
    public DepletedNodeSnapshot[] DepletedNodes { get; set; } = [];
}

public sealed class DepletedNodeSnapshot
{
    public int TileX { get; set; }
    public int TileY { get; set; }
    public string ResourceId { get; set; } = string.Empty;
    public float RegrowTime { get; set; }
}

public sealed class SeasonSnapshot
{
    public string CurrentSeason { get; set; } = "spring";
    public float SeasonProgress { get; set; }
    public float TimeUntilTransition { get; set; }
}