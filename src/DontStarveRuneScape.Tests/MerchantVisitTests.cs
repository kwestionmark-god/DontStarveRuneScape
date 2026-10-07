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
/// Phase-4 remainder, Cycle 1: Merchant visits.
/// Daily at 06:00, factions with standing >= 0.65 spawn a merchant at the colony anchor.
/// Merchant stays for 4 hours (visit window), uses TradeSystem for stock/prices.
/// </summary>
public class MerchantVisitTests
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
            Game.ResourceRegistry = new Data.ResourceRegistry();
            Game.MonsterRegistry = new Data.MonsterRegistry();
            Game.MonsterRegistry.LoadAll();
            Game.SeasonSystem = new SeasonSystem();
            Game.FactionRegistry = new FactionRegistry();
            Game.FactionRegistry.LoadAll();
            Game.QuestSystem = new QuestSystem();
            Game.CaveWorlds = new CaveWorldSystem(Game);
            Game.MerchantVisitSystem = new MerchantVisitSystem(Game);
            
            // Wire TradeSystem
            TradeSystem.Registry = new TradeItemRegistry();
            TradeSystem.Registry.LoadAll();
            TradeSystem.Factions = FactionSystem;
            
            // Load NPC registry (for merchant defs)
            var npcRegistry = new Data.NpcRegistry();
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

    [Fact]
    public void DailyAt0600_FriendlyFactionSpawnsMerchantAtAnchor()
    {
        var h = new Harness();
        h.SetupGame();
        
        // Set up a friendly faction with standing >= 0.65
        var friendlyFaction = new FactionDef
        {
            FactionId = "test_friendly",
            Name = "Test Friendlies",
            BaseHostility = 0f,
            TerritoryBiomes = ["plains"],
        };
        h.Game.FactionRegistry!.Factions["test_friendly"] = friendlyFaction;
        h.Game.FactionSystem!.SetStanding("test_friendly", 0.8f);
        
        // Add a merchant def for this faction/biome
        var merchantDef = new Data.NpcDef
        {
            NpcId = "test_merchant_plains",
            Name = "Test Merchant",
            Type = "merchant",
            Faction = "test_friendly",
            StartingGold = 100,
            PriceModifier = 1.0f,
            CommerceRequirement = 0,
            SpriteKey = "npc/merchant_male_0",
        };
        h.Game.NpcRegistry!.Npcs[merchantDef.NpcId] = merchantDef;
        
        h.SetClockHour(5.9f); // just before 06:00
        h.Tick(250); // tick through 06:00 (10 ticks = 2.5 min, need ~250 for 60 min)
        
        // A merchant NPC should have spawned at the colony anchor (5,5)
        var merchant = h.Npcs.NPCs
            .FirstOrDefault(n => n.NpcType == "merchant" && n.RecruitBehavior == null);
        Assert.NotNull(merchant);
        Assert.Equal("test_friendly", merchant!.FactionId);
        Assert.Equal("plains", merchant.Biome);
        // Position should be at anchor
        Assert.Equal(5.5f * Constants.TileSize, merchant.WorldX, 1);
        Assert.Equal(5.5f * Constants.TileSize, merchant.WorldY, 1);
    }

    [Fact]
    public void Merchant_DepartsAfter4Hours()
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
        
        // Add a merchant def for this faction/biome
        var merchantDef = new Data.NpcDef
        {
            NpcId = "test_merchant_plains",
            Name = "Test Merchant",
            Type = "merchant",
            Faction = "test_friendly",
            StartingGold = 100,
            PriceModifier = 1.0f,
            CommerceRequirement = 0,
            SpriteKey = "npc/merchant_male_0",
        };
        h.Game.NpcRegistry!.Npcs[merchantDef.NpcId] = merchantDef;
        
        h.SetClockHour(5.9f);
        h.Tick(250); // spawn at 06:00
        
        var merchant = h.Npcs.NPCs
            .FirstOrDefault(n => n.NpcType == "merchant");
        Assert.NotNull(merchant);
        
        // Advance 4 hours
        h.SetClockHour(10.1f); // 10:06, past the 4-hour window
        h.Tick(10);
        
        // Merchant should have despawned
        var remaining = h.Npcs.NPCs
            .FirstOrDefault(n => n.NpcType == "merchant" && n.NpcId == merchant!.NpcId);
        Assert.Null(remaining);
    }

    [Fact]
    public void UnfriendlyFaction_DoesNotSpawnMerchant()
    {
        var h = new Harness();
        h.SetupGame();
        
        var hostileFaction = new FactionDef
        {
            FactionId = "test_hostile",
            Name = "Test Hostiles",
            BaseHostility = 1.0f,
            TerritoryBiomes = ["plains"],
        };
        h.Game.FactionRegistry!.Factions["test_hostile"] = hostileFaction;
        h.Game.FactionSystem!.SetStanding("test_hostile", 0.1f); // hostile
        
        h.SetClockHour(5.9f);
        h.Tick(10);
        
        var merchant = h.Npcs.NPCs
            .FirstOrDefault(n => n.NpcType == "merchant");
        Assert.Null(merchant);
    }
}