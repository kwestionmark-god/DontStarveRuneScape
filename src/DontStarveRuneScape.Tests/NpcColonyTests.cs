namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;
using Xunit;

/// <summary>
/// Snapshot restore for recruited residents and their colony needs (colony
/// fusion slice B): the restore path must use the real typed NpcRegistry.
/// </summary>
public class NpcColonyTests
{
    [Fact]
    public void SnapshotRestore_PreservesRecruitmentAndNeeds_AndDerivesStatuses()
    {
        var system = new NPCSystem();
        system.NPCs.Add(new RecruitNpc
        {
            NpcId = "recruit_forest_assistant",
            Name = "Assistant",
            IsRecruited = true,
            RecruitBehavior = "assistant",
            ColonyHunger = 30f,  // Hungry band
            ColonyRest = 10f,    // Exhausted band
        });
        var snapshot = system.GetSnapshot();
        var restored = new NPCSystem();
        var registry = new NpcRegistry();
        registry.LoadAll();
        var world = new World.TileMap(8, 8);
        world.Tiles[3, 3].Biome = new BiomeDef { Id = "forest" };

        restored.RestoreSnapshot(snapshot, registry, world);

        var npc = Assert.Single(restored.NPCs);
        Assert.True(npc.IsRecruited);
        Assert.Equal("assistant", npc.RecruitBehavior);
        Assert.Equal(30f, npc.ColonyHunger);
        Assert.Equal(10f, npc.ColonyRest);
        Assert.Equal("Hungry", npc.ColonyNeedStatus);
        Assert.Equal("Exhausted", npc.ColonyRestStatus);
    }

    [Fact]
    public void SnapshotRestore_OldSnapshots_DefaultToFedAndRested()
    {
        // Pre-colony saves have no hunger/rest fields: 0f in the DTO would
        // read as starving; the snapshot DTO defaults keep them healthy.
        var registry = new NpcRegistry();
        registry.LoadAll();
        var restored = new NPCSystem();
        restored.RestoreSnapshot(new NPCSnapshot
        {
            NPCs = [new NPCDataSnapshot
            {
                NpcId = "recruit_forest_assistant",
                Type = "recruit",
                IsActive = true,
            }],
        }, registry);

        var npc = Assert.Single(restored.NPCs);
        Assert.False(npc.IsRecruited);
        Assert.Equal(100f, npc.ColonyHunger);
        Assert.Equal(100f, npc.ColonyRest);
        Assert.Equal("Fed", npc.ColonyNeedStatus);
        Assert.Equal("Rested", npc.ColonyRestStatus);
    }

    [Fact]
    public void SnapshotRestore_RestoresMerchantBiome_FromSnapshotPosition()
    {
        // Merchant trade stock is biome-keyed; a restored world must keep the
        // biome at the merchant's saved position, like a fresh boot does.
        var registry = new NpcRegistry();
        registry.LoadAll();
        var restored = new NPCSystem();
        var world = new World.TileMap(8, 8);
        world.Tiles[5, 2].Biome = new BiomeDef { Id = "swamp" };
        float x = 5.5f * Config.Constants.TileSize, y = 2.5f * Config.Constants.TileSize;

        restored.RestoreSnapshot(new NPCSnapshot
        {
            NPCs = [new NPCDataSnapshot
            {
                NpcId = "merchant_forest_1",
                Type = "merchant",
                WorldX = x,
                WorldY = y,
                IsActive = true,
            }],
        }, registry, world);

        var npc = Assert.Single(restored.NPCs);
        Assert.Equal("swamp", npc.Biome);
        Assert.Equal(x, npc.WorldX);
        Assert.Equal(y, npc.WorldY);
    }
}
