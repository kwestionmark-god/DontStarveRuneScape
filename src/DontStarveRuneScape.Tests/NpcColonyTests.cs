namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Building;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;
using Xunit;

/// <summary>
/// NPCSystem colony integration from slice B of the colony fusion: real
/// workplace assignment on placed structures (with release of previous
/// assignments) and the recruited/needs snapshot round trip.
/// </summary>
public class NpcColonyTests
{
    private static BuildingSystem BuildSystemWith(params string[] structureIds)
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        var system = new BuildingSystem { Registry = registry };
        foreach (var (id, index) in structureIds.Select((id, i) => (id, i)))
        {
            var def = registry.GetStructure(id);
            system.Structures.Add(new Structure
            {
                StructureId = id,
                StructureDef = def!,
                TileX = 3 + index,
                TileY = 3,
                IsActive = true,
            });
        }
        return system;
    }

    private static NPCSystem SystemWithRecruit(string npcId, string behavior)
    {
        var system = new NPCSystem();
        system.NPCs.Add(new RecruitNpc
        {
            NpcId = npcId,
            Name = npcId,
            IsRecruited = true,
            RecruitBehavior = behavior,
        });
        return system;
    }

    [Fact]
    public void AssignNpcToStructure_AssignsRecruit_AndSetsStatus()
    {
        var system = SystemWithRecruit("r1", "assistant");
        var buildings = BuildSystemWith("crafting_station");

        var (success, message) = system.AssignNpcToStructure("r1", "crafting_station", buildings);

        Assert.True(success, message);
        Assert.Equal("r1", buildings.Structures[0].AssignedNpcId);
        Assert.Equal("Worker assigned", buildings.Structures[0].WorkStatus);
    }

    [Fact]
    public void AssignNpcToStructure_ReleasesPreviousWorkplace()
    {
        var system = SystemWithRecruit("r1", "assistant");
        var buildings = BuildSystemWith("crafting_station", "cooking_station");
        system.AssignNpcToStructure("r1", "crafting_station", buildings);

        var (success, _) = system.AssignNpcToStructure("r1", "cooking_station", buildings);

        Assert.True(success);
        Assert.Null(buildings.Structures[0].AssignedNpcId);
        Assert.Equal("Idle", buildings.Structures[0].WorkStatus);
        Assert.Equal("r1", buildings.Structures[1].AssignedNpcId);
    }

    [Fact]
    public void AssignNpcToStructure_GuardGetsGuardStatus()
    {
        var system = SystemWithRecruit("g1", "guard");
        var buildings = BuildSystemWith("stone_wall");

        Assert.True(system.AssignNpcToStructure("g1", "stone_wall", buildings).Success);
        Assert.Equal("Guard assigned", buildings.Structures[0].WorkStatus);
    }

    [Fact]
    public void AssignNpcToStructure_RejectsUnrecruitedOrMissingNpc()
    {
        var system = new NPCSystem();
        system.NPCs.Add(new RecruitNpc { NpcId = "r1", Name = "r1" });
        var buildings = BuildSystemWith("crafting_station");

        Assert.False(system.AssignNpcToStructure("r1", "crafting_station", buildings).Success);
        Assert.False(system.AssignNpcToStructure("missing", "crafting_station", buildings).Success);
    }

    [Fact]
    public void AssignNpcToStructure_RejectsInactiveNpc_AndMissingBuildings()
    {
        var system = SystemWithRecruit("r1", "assistant");
        system.NPCs[0].IsActive = false;
        var buildings = BuildSystemWith("crafting_station");

        Assert.False(system.AssignNpcToStructure("r1", "crafting_station", buildings).Success);
        system.NPCs[0].IsActive = true;
        Assert.False(system.AssignNpcToStructure("r1", "crafting_station", null).Success);
    }

    [Fact]
    public void AssignNpcToStructure_RejectsStructureHeldByAnotherWorker()
    {
        var system = SystemWithRecruit("r1", "assistant");
        system.NPCs.Add(new RecruitNpc { NpcId = "r2", Name = "r2", IsRecruited = true, RecruitBehavior = "assistant" });
        var buildings = BuildSystemWith("crafting_station");
        buildings.Structures[0].AssignedNpcId = "someone-else";

        Assert.False(system.AssignNpcToStructure("r1", "crafting_station", buildings).Success);
    }

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
