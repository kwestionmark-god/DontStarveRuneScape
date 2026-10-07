namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Building;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Crafting;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Seasons;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.World;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>
/// Phase-5, Cycle 1: full settlement round-trip through SaveSystem. Save a
/// founded colony (anchor, stockpile), a built structure with an assignment
/// and queued work orders, and recruited residents — then load into a fresh
/// Game wired like boot and verify every piece survived.
/// </summary>
public class SettlementPersistenceTests
{
    private sealed class Harness
    {
        public Game Game = new();
        public TileMap Surface = MakeSurface();
        public Player Player = new(4.5f * Constants.TileSize, 4.5f * Constants.TileSize)
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
        public CombatSystem Combat = new();
        public FoodRegistry Foods = MakeFoods();
        public DayNightCycle Clock = new();
        public RecruitmentSystem Recruits = new();
        public WeatherSystem Weather = new();

        private static TileMap MakeSurface(int size = 24)
        {
            var world = new TileMap(size, size);
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                {
                    world.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
                    world.Tiles[x, y].Biome = new BiomeDef { Id = "plains", Name = "Plains" };
                }
            return world;
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

        public void SetupGame()
        {
            Game.World = Surface;
            Game.Player = Player;
            Game.NPCSystem = Npcs;
            Game.ColonySystem = Colony;
            Game.BuildingSystem = Buildings;
            Game.Crafting = Crafting;
            Game.SkillManager = Skills;
            Game.CombatSystem = Combat;
            Game.FoodRegistry = Foods;
            Game.DayNight = Clock;
            Game.RecruitmentSystem = Recruits;
            Game.WeatherSystem = Weather;
            Game.ResourceRegistry = new ResourceRegistry();
            Game.MonsterRegistry = new MonsterRegistry();
            Game.SeasonSystem = new SeasonSystem();
            var npcRegistry = new NpcRegistry();
            npcRegistry.LoadAll();
            Game.NpcRegistry = npcRegistry;
            Game.DataLoader = new DataLoader();
            Game.DataLoader.LoadAll();
            Colony.FoundAt(5.5f * Constants.TileSize, 5.5f * Constants.TileSize, Surface);
        }
    }

    [Fact]
    public void FullColonyRoundTrip_AnchorStockpileAssignmentsOrdersResidents()
    {
        var h = new Harness();
        h.SetupGame();

        // ── Arrange: a living settlement ────────────────────────────────
        // Stockpile with goods
        h.Colony.Store("berry", 5);
        h.Colony.Store("wood", 20);

        // A built campfire outside the colony radius (instant build path)
        var inventory = h.Player.Inventory!;
        inventory.AddItem("stick", 5);
        inventory.AddItem("stone", 5);
        var place = h.Buildings.PlaceStructure("campfire", 20, 20, h.Surface, inventory, h.Skills);
        Assert.True(place.Success, place.Message);
        var campfire = h.Buildings.Structures.Single(s => s.TileX == 20 && s.TileY == 20);
        Assert.False(campfire.IsUnderConstruction);

        // A recruited resident from the real registry
        var def = h.Game.NpcRegistry!.Npcs["recruit_forest_assistant"];
        var scout = Npc.FromDef(def);
        scout.IsRecruited = true;
        scout.RecruitBehavior = "assistant";
        scout.WorldX = 6.5f * Constants.TileSize;
        scout.WorldY = 6.5f * Constants.TileSize;
        h.Npcs.NPCs.Add(scout);

        // Assign the scout to the campfire and queue a work order
        var assign = h.Npcs.AssignNpcToStructure(scout.NpcId, "campfire", h.Buildings);
        Assert.True(assign.Success, assign.Message);
        campfire.WorkRecipeQueue.Add("cooked_fish");
        campfire.WorkStatus = "Waiting for worker";

        // ── Act: save, then load into a fresh game ──────────────────────
        var saveDir = Path.Combine(Path.GetTempPath(), $"dsr-settlement-{Guid.NewGuid():N}");
        var save = new SaveSystem(saveDir);
        save.Save(h.Game, 0);

        var h2 = new Harness();
        h2.SetupGame();
        var data = save.Load(0);
        Assert.NotNull(data);
        save.LoadIntoGame(data!, h2.Game);

        // ── Assert: the settlement survived ────────────────────────────
        // Anchor + stockpile
        Assert.True(h2.Colony.IsFounded);
        Assert.Equal(h.Colony.AnchorTileX, h2.Colony.AnchorTileX);
        Assert.Equal(h.Colony.AnchorTileY, h2.Colony.AnchorTileY);
        Assert.Equal(5, h2.Colony.GetItemQuantity("berry"));
        Assert.Equal(20, h2.Colony.GetItemQuantity("wood"));

        // Structure with assignment + queued orders
        var restoredCampfire = h2.Buildings.Structures
            .Single(s => s.TileX == 20 && s.TileY == 20);
        Assert.Equal("campfire", restoredCampfire.StructureId);
        Assert.Equal(scout.NpcId, restoredCampfire.AssignedNpcId);
        Assert.Contains("cooked_fish", restoredCampfire.WorkRecipeQueue);
        Assert.False(restoredCampfire.IsUnderConstruction);

        // Resident restored from the real registry with recruitment state
        var restoredScout = h2.Npcs.NPCs.Single(n => n.NpcId == scout.NpcId);
        Assert.True(restoredScout.IsRecruited);
        Assert.Equal("assistant", restoredScout.RecruitBehavior);
        Assert.Equal(scout.WorldX, restoredScout.WorldX);
        Assert.Equal(scout.WorldY, restoredScout.WorldY);

        // Player recruit list rewired
        Assert.Contains(scout.NpcId, h2.Player.RecruitedNpcs);
    }
}
