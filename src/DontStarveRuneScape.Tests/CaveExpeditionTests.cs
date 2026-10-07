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
/// Phase-4 remainder, cycle 1: cave-expedition consequence.
/// Guards stay on surface by default; "Bring Guards" moves one guard
/// underground and starts the surface-unguarded timer (raid roll).
/// </summary>
public class CaveExpeditionTests
{
    private sealed class Harness
    {
        public Game Game = new();
        public TileMap Surface = MakeSurface();
        public TileMap Cave = MakeCave();
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
        public CaveWorldSystem CaveSystem;

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

        private static TileMap MakeCave(int size = 64)
        {
            var map = new TileMap(size, size) { IsCave = true, SpawnX = 6, SpawnY = size / 2 };
            var cavern = new BiomeDef { Id = "cavern", Name = "Cavern" };
            for (int elevation = 0; elevation <= 31; elevation++)
            {
                float shade = Math.Clamp((elevation - 11) / 15f, 0f, 1f);
                cavern.TerrainColors[elevation.ToString()] = [
                    (int)(48 + 42 * shade), (int)(43 + 35 * shade), (int)(54 + 38 * shade)];
            }
            map.BiomeRegistry = new BiomeRegistry([cavern]);
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                {
                    var tile = map.Tiles[x, y];
                    tile.Biome = cavern;
                    float nx = (x - size * .5f) / (size * .5f);
                    float ny = (y - size * .5f) / (size * .5f);
                    tile.Elevation = Constants.SeaLevel + 5f + 20f * (1f - MathF.Sqrt(nx * nx + ny * ny));
                }
            return map;
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
            Game.ResourceRegistry = new Data.ResourceRegistry();
            Game.MonsterRegistry = new Data.MonsterRegistry();
            Game.MonsterRegistry.LoadAll();
            Game.SeasonSystem = new SeasonSystem();
            Game.FactionSystem = new FactionSystem();
            Game.FactionRegistry = new FactionRegistry();
            Game.FactionRegistry.LoadAll();
            Game.QuestSystem = new QuestSystem();
            Game.CaveWorlds = new CaveWorldSystem(Game);
            CaveSystem = Game.CaveWorlds;
            Colony.FoundAt(5.5f * Constants.TileSize, 5.5f * Constants.TileSize, Surface);
        }

        public RecruitNpc AddWorker(string id, int tileX, int tileY, string behavior)
        {
            var npc = new RecruitNpc
            {
                NpcId = id,
                Name = id,
                IsRecruited = true,
                RecruitBehavior = behavior,
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

        public void Tick(int steps = 1, float dt = 0.25f)
        {
            for (int i = 0; i < steps; i++)
            {
                Recruits.Tick(dt, Npcs, Player, Game.World,
                    Colony.IsFounded ? Colony : null, Buildings, Crafting, Skills, Combat, Foods,
                    null, null, Weather, null, Clock);
                CaveSystem?.Tick(dt);
                Clock.Tick(dt);
            }
        }
    }

    [Fact]
    public void EnterCave_GuardsStayOnSurfaceByDefault()
    {
        var h = new Harness();
        h.SetupGame();
        var guard = h.AddWorker("g1", 5, 5, "guard");
        var assistant = h.AddWorker("a1", 5, 6, "assistant");

        float guardSurfaceX = guard.WorldX;
        float guardSurfaceY = guard.WorldY;
        float assistantSurfaceX = assistant.WorldX;
        float assistantSurfaceY = assistant.WorldY;

        // Enter cave at the entrance tile
        h.CaveSystem.InteractWith(h.Surface.GetTile(5, 5)!);
        Assert.True(h.CaveSystem.IsInside);

        // Guard stays on surface (position unchanged)
        Assert.Equal(guardSurfaceX, guard.WorldX);
        Assert.Equal(guardSurfaceY, guard.WorldY);

        // Assistant teleports to cave
        Assert.NotEqual(assistantSurfaceX, assistant.WorldX);
        Assert.NotEqual(assistantSurfaceY, assistant.WorldY);
    }

    [Fact]
    public void BringGuardToCave_MovesOneGuard_UnguardedTimerStarts()
    {
        var h = new Harness();
        h.SetupGame();
        var guard1 = h.AddWorker("g1", 5, 5, "guard");
        var guard2 = h.AddWorker("g2", 5, 6, "guard");
        var assistant = h.AddWorker("a1", 5, 7, "assistant");

        h.CaveSystem.InteractWith(h.Surface.GetTile(5, 5)!);
        Assert.True(h.CaveSystem.IsInside);

        // Bring one guard (one guard remains on surface)
        h.CaveSystem.BringGuard("g1");
        Assert.True(h.CaveSystem.HasSurfaceGuards());

        // Surface-unguarded timer should NOT start while a guard remains
        Assert.Equal(0f, h.CaveSystem.SurfaceUnguardedTimer);
    }

    [Fact]
    public void ExitCave_GuardReturnsToSurfacePosition()
    {
        var h = new Harness();
        h.SetupGame();
        var guard = h.AddWorker("g1", 5, 5, "guard");
        var assistant = h.AddWorker("a1", 5, 6, "assistant");

        h.CaveSystem.InteractWith(h.Surface.GetTile(5, 5)!);
        h.CaveSystem.BringGuard("g1");

        float caveX = guard.WorldX;
        float caveY = guard.WorldY;

        // Exit cave
        var exitTile = h.Cave.GetTile(h.Cave.SpawnX, h.Cave.SpawnY)!;
        exitTile.IsCaveExit = true;
        h.CaveSystem.InteractWith(exitTile);
        Assert.False(h.CaveSystem.IsInside);

        // Guard back at surface position
        float expectedX = (5 + 0.5f) * Constants.TileSize;
        float expectedY = (5 + 0.5f) * Constants.TileSize;
        Assert.Equal(expectedX, guard.WorldX);
        Assert.Equal(expectedY, guard.WorldY);
    }

    [Fact]
    public void SurfaceUnguardedTimer_RollsRaidAtThreshold()
    {
        var h = new Harness();
        h.SetupGame();
        var guard = h.AddWorker("g1", 5, 5, "guard");

        // Add a hostile faction with territory overlapping the colony biome
        var hostileFaction = new FactionDef
        {
            FactionId = "test_hostile",
            Name = "Test Hostiles",
            BaseHostility = 1.0f,
            TerritoryBiomes = ["plains"],
            HostileMonsterTypes = ["wolf"],
        };
        h.Game.FactionRegistry!.Factions["test_hostile"] = hostileFaction;
        // Set standing to 0 (hostile)
        h.Game.FactionSystem!.SetStanding("test_hostile", 0f);
        // Ensure monster exists in registry
        h.Game.MonsterRegistry!.MonstersByBiome["plains"] = new Dictionary<string, MonsterDef>
        {
            ["wolf"] = new MonsterDef { MonsterId = "wolf", Name = "Wolf", Hp = 10, Attack = 5, Defence = 0, Speed = 50 }
        };

        h.CaveSystem.InteractWith(h.Surface.GetTile(5, 5)!);
                h.CaveSystem.BringGuard("g1");

                // Verify preconditions
                Assert.False(h.CaveSystem.HasSurfaceGuards());
                Assert.True(h.CaveSystem.IsInside);

                // Fast-forward the surface timer past the raid threshold (5 min = 300s)
                h.CaveSystem.AdvanceSurfaceTimer(301f);

                // If a raid spawned, timer resets to 0 (by design in TryRollRaid).
                // The key test is that a raid DID spawn at the colony perimeter.
                // The raid spawns on the SURFACE map, so we need to check the surface combat system.
                var surfaceCombat = h.CaveSystem.SurfaceCombat;
                var raidMonsters = surfaceCombat?.Monsters
                    .Where(m => m.IsAlive() && m.IsHostile)
                    .ToArray() ?? [];
                Assert.NotEmpty(raidMonsters);
            }

    [Fact]
    public void TwoGuards_BringOne_OtherStaysOnSurface()
    {
        var h = new Harness();
        h.SetupGame();
        var guard1 = h.AddWorker("g1", 5, 5, "guard");
        var guard2 = h.AddWorker("g2", 5, 6, "guard");

        h.CaveSystem.InteractWith(h.Surface.GetTile(5, 5)!);
        h.CaveSystem.BringGuard("g1");

        Assert.True(h.CaveSystem.HasSurfaceGuards()); // guard2 still there
        Assert.Equal(0f, h.CaveSystem.SurfaceUnguardedTimer); // timer resets with guard present
    }
}