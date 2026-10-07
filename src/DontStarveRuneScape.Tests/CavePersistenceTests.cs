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
/// Phase-4 remainder, Cycle 5: cave save/restore (spec section 6).
/// Save mid-expedition with a guard underground: IsInside, entrance tile,
/// surface worker positions, expedition guard list, and the surface
/// unguarded timer all round-trip through SaveData.
/// </summary>
public class CavePersistenceTests
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
        public CaveWorldSystem CaveSystem = null!;

        private static TileMap MakeSurface(int size = 24)
        {
            var world = new TileMap(size, size);
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                {
                    world.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
                    world.Tiles[x, y].Biome = new BiomeDef { Id = "plains", Name = "Plains" };
                }
            world.Tiles[5, 5].IsCaveEntrance = true;
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
            Game.CaveWorlds = new CaveWorldSystem(Game);
            CaveSystem = Game.CaveWorlds;
            Colony.FoundAt(5.5f * Constants.TileSize, 5.5f * Constants.TileSize, Surface);
        }
    }

    [Fact]
    public void CaveSnapshot_RoundTripsExpeditionState()
    {
        var h = new Harness();
        h.SetupGame();

        // Add two guards, send one underground with the player
        var guard1 = new RecruitNpc { NpcId = "g1", Name = "Guard One", IsRecruited = true, RecruitBehavior = "guard", IsActive = true };
        var guard2 = new RecruitNpc { NpcId = "g2", Name = "Guard Two", IsRecruited = true, RecruitBehavior = "guard", IsActive = true };
        h.Npcs.NPCs.Add(guard1);
        h.Npcs.NPCs.Add(guard2);
        h.CaveSystem.InteractWith(h.Surface.GetTile(5, 5)!); // enter cave
        Assert.True(h.CaveSystem.BringGuard("g1"));
        Assert.True(h.CaveSystem.HasSurfaceGuards()); // g2 still on surface

        // Snapshot
        var snap = h.CaveSystem.GetSnapshot();
        Assert.True(snap.IsInside);
        Assert.Equal(5, snap.EntranceX);
        Assert.Equal(5, snap.EntranceY);
        Assert.Contains(snap.GuardsOnExpedition, g => g == "g1");
        Assert.DoesNotContain(snap.GuardsOnExpedition, g => g == "g2");

        // Restore into a fresh system wired to a fresh game (same surface map)
        var h2 = new Harness();
        h2.SetupGame();
        var rGuard1 = new RecruitNpc { NpcId = "g1", Name = "Guard One", IsRecruited = true, RecruitBehavior = "guard", IsActive = true };
        var rGuard2 = new RecruitNpc { NpcId = "g2", Name = "Guard Two", IsRecruited = true, RecruitBehavior = "guard", IsActive = true };
        h2.Npcs.NPCs.Add(rGuard1);
        h2.Npcs.NPCs.Add(rGuard2);
        h2.CaveSystem.RestoreSnapshot(snap, h2.Surface, h2.Combat);
        Assert.True(h2.CaveSystem.IsInside);
        // g1 restored on expedition, g2 on surface
        Assert.True(h2.CaveSystem.IsGuardOnExpedition("g1"));
        Assert.False(h2.CaveSystem.IsGuardOnExpedition("g2"));
    }
}
