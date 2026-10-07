namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Actions;
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
/// Phase-4 remainder, Cycle 3: daily raid roll + diplomacy status messages.
/// Daily at 06:00, hostile factions (standing < 0.25, territory overlapping
/// the colony biome) roll a raid chance; success spawns a 2-4 monster raid
/// party at the colony perimeter on the surface. Merchant arrivals, raids,
/// and standing changes announce through the player notification channel.
/// </summary>
public class DailyRaidTests
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
            ActionSystem = new ActionSystem(),
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
        public FactionSystem FactionSystem = new();
        public TradeSystem TradeSystem = new();
        public DataLoader DataLoader = new();

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
            Game.FactionSystem = FactionSystem;
            Game.TradeSystem = TradeSystem;
            Game.DataLoader = DataLoader;
            Game.ResourceRegistry = new ResourceRegistry();
            Game.MonsterRegistry = new MonsterRegistry();
            Game.MonsterRegistry.LoadAll();
            Game.SeasonSystem = new SeasonSystem();
            Game.FactionRegistry = new FactionRegistry();
            Game.FactionRegistry.LoadAll();
            Game.QuestSystem = new QuestSystem();
            Game.CaveWorlds = new CaveWorldSystem(Game);
            Game.MerchantVisitSystem = new MerchantVisitSystem(Game);

            TradeSystem.Registry = new TradeItemRegistry();
            TradeSystem.Registry.LoadAll();
            TradeSystem.Factions = FactionSystem;

            var npcRegistry = new NpcRegistry();
            npcRegistry.LoadAll();
            Game.NpcRegistry = npcRegistry;

            Colony.FoundAt(5.5f * Constants.TileSize, 5.5f * Constants.TileSize, Surface);
        }

        public void Tick(int steps = 1, float dt = 0.25f)
        {
            for (int i = 0; i < steps; i++)
            {
                Recruits.Tick(dt, Npcs, Player, Game.World,
                    Colony.IsFounded ? Colony : null, Buildings, Crafting, Skills, Combat, Foods,
                    null, null, Weather, null, Clock);
                Game.CaveWorlds?.Tick(dt);
                Game.MerchantVisitSystem?.Tick(Clock.HourOfDay);
                Clock.Tick(dt);
            }
        }

        public void SetClockHour(float hour)
            => Clock.RestoreSnapshot(new DayNightSnapshot { TimeOfDay = (hour - 6f) / 24f });
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static void AddHostileFaction(Harness h, string factionId, float standing)
    {
        var faction = new FactionDef
        {
            FactionId = factionId,
            Name = "Test Hostiles",
            BaseHostility = 1f,
            TerritoryBiomes = ["plains"],
            HostileMonsterTypes = ["wolf"],
        };
        h.Game.FactionRegistry!.Factions[factionId] = faction;
        h.Game.FactionSystem!.SetStanding(factionId, standing);
        h.Game.MonsterRegistry!.MonstersByBiome["plains"] = new Dictionary<string, MonsterDef>
        {
            ["wolf"] = new MonsterDef { MonsterId = "wolf", Name = "Wolf", Hp = 10, Attack = 5, Defence = 0, Speed = 50 },
        };
    }

    // ─── Tests ───────────────────────────────────────────────────────────

    [Fact]
    public void DailyAt0600_HostileFaction_RaidPartySpawnsAtColonyPerimeter()
    {
        var h = new Harness();
        h.SetupGame();
        AddHostileFaction(h, "test_hostile", 0.1f);
        h.Game.MerchantVisitSystem!.RaidRollOverride = true; // deterministic

        h.SetClockHour(5.9f);
        h.Tick(250); // cross 06:00

        var raidMonsters = h.Combat.Monsters
            .Where(m => m.MonsterId == "wolf")
            .ToList();
        // 2-4 raiders; ring points are clamped to the map so the party
        // always materializes even near a map edge.
        Assert.InRange(raidMonsters.Count, 2, 4);

        // Spawn ring: ~9 tiles from the anchor (5,5), on the surface map
        // (clamped edge points may sit closer than the nominal 9-tile ring)
        foreach (var m in raidMonsters)
        {
            float dx = m.WorldX / Constants.TileSize - 5.5f;
            float dy = m.WorldY / Constants.TileSize - 5.5f;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            Assert.InRange(dist, 4f, 13f);
        }
    }

    [Fact]
    public void DailyAt0600_NeutralFaction_NoRaid()
    {
        var h = new Harness();
        h.SetupGame();
        AddHostileFaction(h, "test_hostile", 0.5f); // neutral standing
        h.Game.MerchantVisitSystem!.RaidRollOverride = true; // even forced, standing gate blocks

        h.SetClockHour(5.9f);
        h.Tick(250); // cross 06:00

        Assert.Empty(h.Combat.Monsters.Where(m => m.MonsterId == "wolf"));
    }

    [Fact]
    public void MerchantArrival_SendsNotification()
    {
        var h = new Harness();
        h.SetupGame();

        var friendlyFaction = new FactionDef
        {
            FactionId = "test_friendly",
            Name = "Test Friendlies",
            BaseHostility = 0f,
            TerritoryBiomes = ["plains"],
        };
        h.Game.FactionRegistry!.Factions["test_friendly"] = friendlyFaction;
        h.Game.FactionSystem!.SetStanding("test_friendly", 0.8f);

        var merchantDef = new NpcDef
        {
            NpcId = "test_merchant_plains",
            Name = "Test Merchant",
            Type = "merchant",
            Faction = "test_friendly",
            StartingGold = 100,
            PriceModifier = 1f,
            CommerceRequirement = 0,
            SpriteKey = "npc/merchant_male_0",
        };
        h.Game.NpcRegistry!.Npcs[merchantDef.NpcId] = merchantDef;

        h.SetClockHour(5.9f);
        h.Tick(250); // cross 06:00

        h.Game.Player!.ActionSystem!.FlushNotifications();
        Assert.Contains(h.Game.Player!.ActionSystem!.Notifications, n =>
            n.Text.Contains("Merchants from Test Friendlies arrived"));
    }

    [Fact]
    public void RaidSpawn_SendsRaidIncomingNotification()
    {
        var h = new Harness();
        h.SetupGame();
        AddHostileFaction(h, "test_hostile", 0.1f);
        h.Game.MerchantVisitSystem!.RaidRollOverride = true; // deterministic

        h.SetClockHour(5.9f);
        h.Tick(250); // cross 06:00

        h.Game.Player!.ActionSystem!.FlushNotifications();
        Assert.Contains(h.Game.Player!.ActionSystem!.Notifications, n =>
            n.Text.Contains("Raid incoming"));
    }
}

