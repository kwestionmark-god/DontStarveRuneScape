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
/// The logistics slice: hauling is real work. Gathered goods ride with the
/// worker to the stockpile, gather tiles are reserved on the colony task
/// board so two workers never harvest the same node, reservations release on
/// interruption and completion, a full stockpile blocks delivery without
/// losing goods, and carried goods persist through save snapshots.
/// </summary>
public class ColonyLogisticsTests
{
    private sealed class Harness
    {
        public TileMap World = MakeWorld();
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
        public FoodRegistry Foods = MakeFoods();
        public RecruitmentSystem Recruits = new();
        public StructureDefRegistry StructureDefs { get; } = MakeStructureDefs();

        private static TileMap MakeWorld(int size = 24)
        {
            var world = new TileMap(size, size);
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    world.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
            return world;
        }

        private static StructureDefRegistry MakeStructureDefs()
        {
            var registry = new StructureDefRegistry();
            registry.LoadAll();
            return registry;
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

        private static FoodRegistry MakeFoods()
        {
            var loader = new DataLoader();
            loader.LoadAll();
            return new FoodRegistry(loader.ItemsData);
        }

        public RecruitNpc AddWorker(string id, int tileX, int tileY,
            float hunger = 100f, float rest = 100f)
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
                ColonyHunger = hunger,
                ColonyRest = rest,
            };
            Npcs.NPCs.Add(npc);
            return npc;
        }

        public bool FoundColony(int x = 5, int y = 5)
            => Colony.FoundAt((x + 0.5f) * Constants.TileSize, (y + 0.5f) * Constants.TileSize, World);

        public void Tick(int count, float dt = 0.25f)
        {
            for (int i = 0; i < count; i++)
                Recruits.Tick(dt, Npcs, Player, World,
                    Colony.IsFounded ? Colony : null, Buildings, Crafting, Skills, null, Foods);
        }
    }

    private static ResourceNode NodeAt(TileMap world, int x, int y, string yieldItem, int yield = 2)
    {
        var node = new ResourceNode("test_" + yieldItem + $"_{x}_{y}", new ResourceDef
        {
            Id = "test_" + yieldItem,
            Name = yieldItem,
            YieldItem = yieldItem,
            Yield = yield,
            Xp = 1f,
            DepletionCount = 1,
            Seasons = [],
        }, 1f);
        world.GetTile(x, y)!.ResourceNode = node;
        return node;
    }

    [Fact]
    public void GatheredGoods_RideWithWorker_ThenLandInTheStockpile()
    {
        var h = new Harness();
        Assert.True(h.FoundColony());
        var worker = h.AddWorker("a1", 5, 6);
        NodeAt(h.World, 8, 5, "berries", yield: 2);

        // Step to the harvest moment: goods appear on the worker, and the
        // stockpile stays empty until the haul home completes.
        bool harvested = false;
        for (int i = 0; i < 120 && !harvested; i++)
        {
            h.Tick(1);
            harvested = worker.CarriedQuantity > 0;
        }
        Assert.True(harvested, "worker never harvested");
        Assert.Equal("berries", worker.CarriedItemId);
        Assert.Equal(2, worker.CarriedQuantity);
        Assert.Equal(0, h.Colony.GetItemQuantity("berries"));
        // The deposit task appears on the next tick, when the worker takes
        // up the haul home.
        h.Tick(1);
        Assert.Contains(h.Colony.TaskBoard.Tasks,
            task => task.Kind == ColonyTaskKind.Deposit && task.AssigneeNpcId == "a1");

        // Step to the deposit: carried clears and the stockpile receives
        // exactly the harvested amount.
        bool deposited = false;
        for (int i = 0; i < 120 && !deposited; i++)
        {
            h.Tick(1);
            deposited = worker.CarriedQuantity == 0 && h.Colony.GetItemQuantity("berries") > 0;
        }
        Assert.True(deposited, "worker never deposited the haul");
        Assert.Equal(2, h.Colony.GetItemQuantity("berries"));
        Assert.DoesNotContain(h.Colony.TaskBoard.Tasks, task => task.AssigneeNpcId == "a1");
    }

    [Fact]
    public void TaskBoard_ReservesTiles_OnlyOneWorkerClaimsANode()
    {
        var h = new Harness();
        Assert.True(h.FoundColony());
        h.AddWorker("a1", 5, 6);
        h.AddWorker("a2", 5, 4);
        var node = NodeAt(h.World, 6, 5, "berries", yield: 2);

        // The first worker to pass claims the tile; the second never gets it
        // and the reservation is visible on the board.
        bool reserved = false;
        for (int i = 0; i < 40 && !reserved; i++)
        {
            h.Tick(1);
            reserved = h.Colony.TaskBoard.IsTileReserved(6, 5);
        }
        Assert.True(reserved, "no gather reservation appeared");
        var reservation = h.Colony.TaskBoard.FindGatherByTile(6, 5);
        Assert.NotNull(reservation);
        Assert.Equal(ColonyTaskKind.Gather, reservation!.Kind);
        Assert.Contains(reservation.AssigneeNpcId, new[] { "a1", "a2" });
        Assert.Equal("berries", reservation.ItemId);
        // A different worker cannot claim the reserved tile.
        string other = reservation.AssigneeNpcId == "a1" ? "a2" : "a1";
        Assert.Null(h.Colony.TaskBoard.ClaimGather(other, 6, 5, "berries"));

        // The single node depletes exactly once — no double harvest — and
        // the haul home lands the goods before the loop ends.
        bool delivered = false;
        for (int i = 0; i < 200 && !delivered; i++)
        {
            h.Tick(1);
            delivered = node.IsDepleted && h.Colony.GetItemQuantity("berries") == 2;
        }
        Assert.True(delivered, "single node never delivered exactly one yield");
        Assert.False(h.Colony.TaskBoard.IsTileReserved(6, 5));
    }

    [Fact]
    public void RestInterruption_ReleasesReservation_ForAnotherWorker()
    {
        var h = new Harness();
        Assert.True(h.FoundColony());
        var tired = h.AddWorker("a1", 5, 6);
        var fresh = h.AddWorker("a2", 6, 7);
        var node = NodeAt(h.World, 8, 6, "berries", yield: 2);

        // The first worker claims the node and starts walking.
        bool reservedByA1 = false;
        for (int i = 0; i < 40 && !reservedByA1; i++)
        {
            h.Tick(1);
            reservedByA1 = h.Colony.TaskBoard.FindGatherByTile(8, 6)?.AssigneeNpcId == "a1";
        }
        Assert.True(reservedByA1, "first worker never claimed the node");

        // Exhaust them mid-walk: the rest branch must release the claim so
        // it does not leak while they recover. The released tile may be
        // re-claimed by the other worker in the same pass.
        tired.ColonyRest = 5f;
        h.Tick(1);
        Assert.Contains(tired.ColonyRestStatus, new[] { "Seeking rest", "Resting" });
        Assert.NotEqual("a1", h.Colony.TaskBoard.FindGatherByTile(8, 6)?.AssigneeNpcId);

        // The fresh worker can now take the released node and deliver.
        bool delivered = false;
        for (int i = 0; i < 240 && !delivered; i++)
        {
            h.Tick(1);
            delivered = node.IsDepleted && h.Colony.GetItemQuantity("berries") == 2;
        }
        Assert.True(delivered, "second worker never completed the released node");
    }

    [Fact]
    public void FullStockpile_BlocksDelivery_WithoutLosingGoods()
    {
        var h = new Harness();
        Assert.True(h.FoundColony());
        // Shrink the stockpile so a second delivery has nowhere to go.
        h.Colony.RestoreSnapshot(new ColonySnapshot
        {
            IsFounded = true,
            AnchorTileX = 5,
            AnchorTileY = 5,
            WorkRadiusTiles = 10,
            StorageCapacity = 3,
        }, h.World);
        var worker = h.AddWorker("a1", 5, 6);
        NodeAt(h.World, 7, 5, "sticks", yield: 2);

        bool harvested = false;
        for (int i = 0; i < 120 && !harvested; i++)
        {
            h.Tick(1);
            harvested = worker.CarriedQuantity > 0;
        }
        Assert.True(harvested, "worker never harvested");
        Assert.Equal("sticks", worker.CarriedItemId);

        // Fill the pile while the worker walks home.
        Assert.True(h.Colony.Store("sticks", 3));

        bool blocked = false;
        for (int i = 0; i < 120 && !blocked; i++)
        {
            h.Tick(1);
            blocked = worker.CarryStatus == "Stockpile full";
        }
        Assert.True(blocked, "worker never reported the full stockpile");
        Assert.Equal(2, worker.CarriedQuantity);
        Assert.Equal(3, h.Colony.GetItemQuantity("sticks"));

        // Free space and the blocked haul completes.
        Assert.True(h.Colony.RemoveItem("sticks", 2));
        bool delivered = false;
        for (int i = 0; i < 120 && !delivered; i++)
        {
            h.Tick(1);
            delivered = worker.CarriedQuantity == 0;
        }
        Assert.True(delivered, "blocked haul never completed after space freed");
        Assert.Equal(3, h.Colony.GetItemQuantity("sticks"));
    }

    [Fact]
    public void CarriedGoods_SurviveNpcSaveAndRestore()
    {
        var npcs = new NPCSystem();
        var original = new RecruitNpc
        {
            NpcId = "recruit_forest_assistant",
            IsRecruited = true,
            RecruitBehavior = "assistant",
            IsActive = true,
            CarriedItemId = "sticks",
            CarriedQuantity = 4,
        };
        npcs.NPCs.Add(original);

        var snapshot = npcs.GetSnapshot();
        var restored = new NPCSystem();
        restored.RestoreSnapshot(snapshot, MakeRegistry());

        var worker = Assert.Single(restored.NPCs);
        Assert.Equal("sticks", worker.CarriedItemId);
        Assert.Equal(4, worker.CarriedQuantity);
    }

    private static NpcRegistry MakeRegistry()
    {
        var registry = new NpcRegistry();
        registry.LoadAll();
        return registry;
    }
}
