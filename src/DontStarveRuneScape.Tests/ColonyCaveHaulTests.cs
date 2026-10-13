namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Building;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Crafting;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.World;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>
/// Colony cave-haul slice: the last surface↔cave route — a cave-expedition
/// worker carrying goods walks to the cave EXIT and hands them off to the
/// surface colony stockpile (the exit is the route). Before this, cave
/// workers could only gather, then idled forever with a full pack
/// (haulsHome was gated !inCave).
/// </summary>
public class ColonyCaveHaulTests
{
    private sealed class Harness
    {
        public TileMap Cave = MakeCave();
        public Player Player = new(3.5f * Constants.TileSize, 3.5f * Constants.TileSize)
        {
            Gear = new PlayerGear(),
            SkillManager = new SkillManager(),
            Inventory = new Inv(),
            Survival = new SurvivalSystem(),
        };
        public NPCSystem Npcs = new();
        public ColonySystem Colony = new();
        public BuildingSystem Buildings = MakeBuildings();
        public CraftingSystem Crafting = MakeCrafting();
        public SkillManager Skills = new();
        public RecruitmentSystem Recruits = new();

        private static TileMap MakeCave()
        {
            var cave = new TileMap(16, 16) { IsCave = true, SpawnX = 3, SpawnY = 8 };
            for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++)
                cave.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
            cave.BuildCornerElevations();
            return cave;
        }

        private static BuildingSystem MakeBuildings()
        {
            var registry = new StructureDefRegistry();
            registry.LoadAll();
            return new BuildingSystem { Registry = registry };
        }

        private static CraftingSystem MakeCrafting()
        {
            var recipes = new RecipeRegistry();
            recipes.LoadAll();
            return new CraftingSystem { Registry = recipes };
        }

        public RecruitNpc AddWorker(string id, int tileX, int tileY)
        {
            var npc = new RecruitNpc
            {
                NpcId = id,
                Name = id,
                IsRecruited = true,
                RecruitBehavior = "assistant",
                WorldX = (tileX + 0.5f) * Constants.TileSize,
                WorldY = (tileY + 0.5f) * Constants.TileSize,
                Health = 100,
                MaxHealth = 100,
                IsActive = true,
                ColonyHunger = 100f,
                ColonyRest = 100f,
            };
            Npcs.NPCs.Add(npc);
            return npc;
        }

        public void Tick(int count, float dt = 0.25f)
        {
            for (int i = 0; i < count; i++)
                Recruits.Tick(dt, Npcs, Player, Cave,
                    Colony.IsFounded ? Colony : null, Buildings, Crafting, Skills, null, null);
        }
    }

    [Fact]
    public void CaveWorker_CarryingGoods_DepositsAtTheExit()
    {
        var h = new Harness();
        h.Colony.FoundAt(0f, 0f, h.Cave); // anchor coords live in world px; cave-local is fine data-wise
        var worker = h.AddWorker("w1", 10, 8);
        worker.CarriedItemId = "copper_ore";
        worker.CarriedQuantity = 2;

        h.Tick(400); // enough to walk from (10,8) to the exit (3,8)

        Assert.Null(worker.CarriedItemId);
        Assert.Equal(0, worker.CarriedQuantity);
        Assert.Equal(2, h.Colony.GetItemQuantity("copper_ore"));
        // Delivered at the exit, not the anchor.
        float exitX = (3 + 0.5f) * Constants.TileSize;
        Assert.InRange(MathF.Abs(worker.WorldX - exitX), 0f, Constants.TileSize * 2f);
    }

    [Fact]
    public void CaveWorker_NoColony_KeepsGathering_AsBefore()
    {
        var h = new Harness(); // colony never founded
        var worker = h.AddWorker("w1", 10, 8);
        worker.CarriedItemId = "copper_ore";
        worker.CarriedQuantity = 2;

        h.Tick(40);

        // Old law intact: without a colony there is no stockpile to hand
        // off to — the worker keeps the goods and resumes cave work/wander.
        Assert.Equal(2, worker.CarriedQuantity);
        Assert.False(h.Colony.IsFounded);
    }

    [Fact]
    public void CaveWorker_FullStockpile_KeepsGoods_AndSaysSo()
    {
        var h = new Harness();
        h.Colony.FoundAt(0f, 0f, h.Cave);
        // Fill the stockpile to the brim.
        var cap = h.Colony.FreeCapacity;
        h.Colony.Store("dirt_brick", cap);
        Assert.Equal(0, h.Colony.FreeCapacity);

        var worker = h.AddWorker("w1", 10, 8);
        worker.CarriedItemId = "copper_ore";
        worker.CarriedQuantity = 2;

        h.Tick(400);

        Assert.Equal(2, worker.CarriedQuantity); // goods never lost to a full stockpile
        Assert.Equal(0, h.Colony.GetItemQuantity("copper_ore"));
        Assert.Contains("full", h.Colony.TaskBoard.Tasks
            .Where(t => t.Kind == ColonyTaskKind.Deposit)
            .Select(t => t.Status ?? string.Empty)
            .FirstOrDefault(string.Empty) ?? worker.CarryStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SurfaceWorker_Unaffected_StillHaulsToAnchor()
    {
        // Regression guard: surface behavior unchanged by the cave route.
        var h = new Harness();
        h.Cave.IsCave = false; // treat the same flat map as surface
        h.Colony.FoundAt(0f, 0f, h.Cave);
        var worker = h.AddWorker("w1", 10, 8);
        worker.CarriedItemId = "copper_ore";
        worker.CarriedQuantity = 2;

        h.Tick(400);

        Assert.Null(worker.CarriedItemId);
        Assert.Equal(2, h.Colony.GetItemQuantity("copper_ore"));
    }
}
