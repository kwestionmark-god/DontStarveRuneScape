namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Crafting;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.World;
using Xunit;
using Inv = DontStarveRuneScape.Inventory.Inventory;

/// <summary>
/// Fishing slice: rod gate + fishing skill (spec:
/// docs/superpowers/specs/2026-10-08-fishing-rod-skill-design.md).
/// Player-side tests are harness-free (ActionSystem shape); worker-side
/// tests use the WaterBucketTests harness shape.
/// </summary>
public class FishingTests
{
    // ─── Shared fixtures ─────────────────────────────────────────────────

    /// <summary>The real fish_spot def, exactly as resources.json loads it
    /// post-slice (rod-gated, level 1).</summary>
    private static ResourceDef FishSpotDef() => new()
    {
        Id = "fish_spot",
        Name = "Fish Spot",
        Biome = "coastal",
        Category = "special",
        YieldItem = "raw_fish",
        Yield = 1,
        Xp = 15f,
        DepletionCount = 2,
        Regrow = 60f,
        ToolRequirement = "fishing_rod",  // the gate under test
        RequiredLevel = 1,
        Seasons = [],
    };

    private static ResourceNode FishSpot() =>
        new("fish_spot", FishSpotDef(), 10f);

    private static void PumpSuccessRate(SkillManager sm, float value)
        => sm.GetSkill("fishing").SubStats["success_rate"] = value;

    // ─── 1. Data shape ──────────────────────────────────────────────────

    [Fact]
    public void FishingRodData_ItemAndRecipe_LoadFromJson()
    {
        var items = DataLoader.LoadJsonList<ItemDef>(Constants.ItemsFile, "items");
        var rod = items.FirstOrDefault(i => i.Id == "fishing_rod");
        Assert.NotNull(rod);
        Assert.Equal("fishing_rod", rod!.ToolType);
        Assert.True(rod.IsEquippable);
        Assert.True(rod.IsEssentialTool);
        Assert.Equal(100, rod.Durability);
        Assert.Equal("items/elder_wood_staff", rod.SpriteKey);

        var recipes = new RecipeRegistry();
        recipes.LoadAll();
        var recipe = recipes.GetRecipe("craft_fishing_rod");
        Assert.NotNull(recipe);
        Assert.Equal("fishing_rod", recipe!.OutputItem);
        Assert.Equal("crafting", recipe.RequiredSkill);
        Assert.Equal(2, recipe.RequiredLevel);
        Assert.Contains(recipe.Inputs, i => i.ItemId == "planks" && i.Quantity == 2);
        Assert.Contains(recipe.Inputs, i => i.ItemId == "grass_rope" && i.Quantity == 1);
    }

    [Fact]
    public void FishSpotDef_RequiresRodGate()
    {
        var loader = new DataLoader();
        loader.LoadAll();
        // Raw parsed row — the same rows Bootstrap feeds its ResourceDef
        // conversion (Bootstrap.cs:53-83), asserted directly.
        var row = loader.ResourcesData.FirstOrDefault(r =>
            r.TryGetValue("id", out var id) && id.ToString() == "fish_spot");
        Assert.NotNull(row);
        Assert.True(row!.TryGetValue("requires_tool", out var tool));
        // RED until the gate lands: today requires_tool is JSON null.
        Assert.Equal("fishing_rod", tool?.ToString());
        Assert.True(row.TryGetValue("required_level", out var level));
        // Level 5 would strand the only fishing XP source behind a
        // fishing-level gate nothing else can train. Entry-level, rod-gated.
        Assert.Equal("1", level?.ToString());
    }

    [Fact]
    public void FishingSkill_IsRegisteredInSkillManager()
    {
        var sm = new SkillManager();
        // Registration is what makes XP real: AddXpWithNotification
        // no-ops for unknown ids (SkillManager.cs:73).
        var messages = sm.AddXpWithNotification("fishing", 50f);
        Assert.Equal(50f, sm.GetSkill("fishing").Xp);
        Assert.Equal(1, sm.GetSkillLevel("fishing"));
    }

    // ─── 2–3. Player path ───────────────────────────────────────────────

    [Fact]
    public void PlayerFishing_RefusedWithoutRod()
    {
        var system = new ActionSystem();
        var node = FishSpot();
        var sm = new SkillManager();
        var inv = new Inv();

        var error = system.StartAction(ActionType.Fishing, node, sm, inv);
        Assert.Equal("You need a fishing_rod.", error);
        Assert.False(system.Active.IsBusy);
    }

    [Fact]
    public void PlayerFishing_SucceedsWithCarriedRod_TrainsFishingNotForaging()
    {
        var system = new ActionSystem();
        var node = FishSpot();
        var sm = new SkillManager();
        PumpSuccessRate(sm, 1000f); // deterministic success roll
        var inv = new Inv();
        Assert.True(inv.AddItem("fishing_rod", 1)); // carried, not equipped

        Assert.Null(system.StartAction(ActionType.Fishing, node, sm, inv));
        Assert.True(system.Active.IsBusy);

        // The cast is timed (Constants.FishingCastSeconds): no catch mid-cast…
        float remaining = Constants.FishingCastSeconds;
        while (remaining > 0.25f)
        {
            Assert.Null(system.Update(0.25f));
            remaining -= 0.25f;
        }

        // …the tick that closes the window is the catch tick.
        var result = system.Update(0.25f);
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("raw_fish", result.ItemId);
        Assert.Equal(1, result.Quantity);
        // fish_spot xp_reward is 15 — and it must route to FISHING, not
        // foraging (the ActionTypeToSkillId landmine: CompleteAction nulls
        // Active.Resource, so only the enum survives to tell the skill).
        Assert.Equal(15f, result.Xp);

        // No repeated completion on later ticks.
        Assert.Null(system.Update(0.016f));

        // ProcessCompletion lands the XP in fishing — and nowhere else.
        system.ProcessCompletion(result, inv, sm);
        Assert.Equal(15f, sm.GetSkill("fishing").Xp);
        Assert.Equal(0f, sm.GetSkill("foraging").Xp);
        Assert.True(inv.Slots.Any(s => s?.ItemId == "raw_fish"),
            "raw_fish should be in the player inventory after a catch");
    }

    [Fact]
    public void PlayerFishing_MidCastIsBusyAndHoldsSilence()
    {
        var system = new ActionSystem();
        var node = FishSpot();
        var sm = new SkillManager();
        var inv = new Inv();
        Assert.True(inv.AddItem("fishing_rod", 1));

        Assert.Null(system.StartAction(ActionType.Fishing, node, sm, inv));

        // Mid-cast the player is busy: a second start is refused…
        Assert.Equal("Already performing an action.",
            system.StartAction(ActionType.Fishing, node, sm, inv));
        // …and partial-time Updates return null (no early catch, no leak).
        Assert.Null(system.Update(0.5f));
        Assert.Null(system.Update(1.0f));
        Assert.True(system.Active.IsBusy);
        Assert.Null(system.Update(1.4f));
        // Total elapsed 2.9s < 3.0s cast: still busy.
        Assert.True(system.Active.IsBusy);
    }

    [Fact]
    public void PlayerFishing_CancelActive_StopsTheCast()
    {
        var system = new ActionSystem();
        var node = FishSpot();
        var sm = new SkillManager();
        var inv = new Inv();
        Assert.True(inv.AddItem("fishing_rod", 1));

        Assert.Null(system.StartAction(ActionType.Fishing, node, sm, inv));
        Assert.True(system.Active.IsBusy);

        // Walk-away mid-cast: the cast cancels with no yield.
        Assert.True(system.CancelActive());
        Assert.False(system.Active.IsBusy);
        Assert.Null(system.Active.Resource);

        // Cancelled casts never resolve: Updates stay null forever.
        Assert.Null(system.Update(0.25f));
        Assert.Null(system.Update(5.0f));
        Assert.True(inv.Slots.All(s => s?.ItemId != "raw_fish"),
            "a cancelled cast must not yield");
        Assert.Equal(0f, sm.GetSkill("fishing").Xp);

        // Cancelling an idle action is a no-op (returns false).
        Assert.False(system.CancelActive());
        Assert.False(system.Active.IsBusy);

        // And the player can immediately start a fresh cast.
        Assert.Null(system.StartAction(ActionType.Fishing, node, sm, inv));
        Assert.True(system.Active.IsBusy);
    }

    [Fact]
    public void PlayerForaging_StillInstant_RegressionGuard()
    {
        var system = new ActionSystem();
        var sm = new SkillManager();
        sm.GetSkill("foraging").SubStats["success_rate"] = 1000f;
        var inv = new Inv();
        var berries = new ResourceDef
        {
            Id = "berry_bush",
            Name = "Berry Bush",
            YieldItem = "berries",
            Yield = 1,
            Xp = 5f,
            DepletionCount = 3,
            RequiredLevel = 1,
            Seasons = [],
        };
        var node = new ResourceNode("berry_bush", berries, 3f);

        Assert.Null(system.StartAction(ActionType.Foraging, node, sm, inv));
        // Every gather EXCEPT fishing stays instant: the very first Update
        // completes it (no DurationRemaining was ever set).
        var result = system.Update(0.016f);
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("berries", result.ItemId);
    }

    // ─── 5–7. Worker path (WaterBucketTests harness shape) ──────────────

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

        private static TileMap MakeWorld(int size = 24)
        {
            var world = new TileMap(size, size);
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    world.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
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
            Recruits.OnRecruit(id, "assistant");
            return npc;
        }

        public bool FoundColony(int x = 5, int y = 5)
            => Colony.FoundAt((x + 0.5f) * Constants.TileSize,
                (y + 0.5f) * Constants.TileSize, World);

        public void Tick(int n)
        {
            for (int i = 0; i < n; i++)
                Recruits.Tick(0.25f, Npcs, Player, World, Colony,
                    Buildings, Crafting, Skills, null, Foods, null, null, null, null, null);
        }
    }

    private static ResourceNode FishSpotAt(TileMap world, int x, int y)
    {
        var node = new ResourceNode("fish_spot", FishSpotDef(), 10f);
        world.GetTile(x, y)!.ResourceNode = node;
        return node;
    }

    [Fact]
    public void WorkerFishing_NoRod_NeverCarries()
    {
        var h = new Harness();
        h.FoundColony(5, 5);
        FishSpotAt(h.World, 6, 6);
        var worker = h.AddWorker("fisher", 5, 6);

        h.Tick(12);

        // No rod anywhere (player inv empty, colony store empty) → the
        // node is never claimed, nothing is ever carried.
        Assert.Equal(0, worker.CarriedQuantity);
        Assert.True(string.IsNullOrEmpty(worker.CarriedItemId));
    }

    [Fact]
    public void WorkerFishing_RodInColonyStore_HarvestsAndTrainsFishing()
    {
        var h = new Harness();
        h.FoundColony(5, 5);
        FishSpotAt(h.World, 6, 6);
        h.Colony.Store("fishing_rod", 1);
        var worker = h.AddWorker("fisher", 5, 6);

        h.Tick(40); // walk + harvest + haul home

        // Outcome assert: raw_fish landed in the colony stockpile.
        Assert.True(h.Colony.GetItemQuantity("raw_fish") > 0,
            $"expected raw_fish in store, got {h.Colony.GetItemQuantity("raw_fish")}");
        // The rod is a gate, not a consumable — it stays.
        Assert.True(h.Colony.GetItemQuantity("fishing_rod") > 0);
        // The worker trains FISHING (GatherSkillFor branch), not mining
        // (the old any-tool-node → mining trap) and not foraging.
        Assert.True(worker.Skills.GetSkill("fishing").Xp > 0f,
            $"expected fishing xp > 0, got {worker.Skills.GetSkill("fishing").Xp}");
        Assert.Equal(0f, worker.Skills.GetSkill("mining").Xp);
    }

    [Fact]
    public void WorkerPickaxeNodes_StillTrainMining()
    {
        var h = new Harness();
        h.FoundColony(5, 5);
        // Surface copper + colony pickaxe — the existing surface-tool
        // carve-out must keep working (regression guard for the
        // GatherSkillFor re-order).
        var copper = new ResourceDef
        {
            Id = "copper_rock",
            Name = "Copper Rock",
            YieldItem = "copper_ore",
            Yield = 1,
            RequiredLevel = 1,
            ToolRequirement = "pickaxe",
            Xp = 35f,
            DepletionCount = 5,
            Seasons = [],
        };
        h.World.GetTile(6, 6)!.ResourceNode =
            new ResourceNode("copper_rock", copper, 10f);
        h.Colony.Store("pickaxe", 1);
        var miner = h.AddWorker("miner", 6, 7);

        h.Tick(40);

        Assert.True(miner.Skills.GetSkill("mining").Xp > 0f,
            "surface pickaxe harvest still trains mining");
        Assert.True(h.Colony.GetItemQuantity("copper_ore") > 0);
    }

    // ─── Bait: prepared casts + colony bait logistics ────────────────────

    [Fact]
    public void BaitData_ItemAndRecipe_LoadFromJson()
    {
        var items = DataLoader.LoadJsonList<ItemDef>(Constants.ItemsFile, "items");
        var bait = items.FirstOrDefault(i => i.Id == "fishing_bait");
        Assert.NotNull(bait);
        Assert.Equal(20, bait!.StackSize);
        Assert.False(bait.IsEquippable);
        Assert.False(bait.IsEssentialTool);
        Assert.False(bait.IsFood);
        Assert.Equal("items/worm_segment", bait.SpriteKey);

        var recipes = new RecipeRegistry();
        recipes.LoadAll();
        var recipe = recipes.GetRecipe("craft_fishing_bait");
        Assert.NotNull(recipe);
        Assert.Equal("fishing_bait", recipe!.OutputItem);
        Assert.Equal(4, recipe.OutputQuantity);
        Assert.Equal("crafting", recipe.RequiredSkill);
        Assert.Equal(1, recipe.RequiredLevel);
        Assert.Contains(recipe.Inputs, i => i.ItemId == "shells" && i.Quantity == 2);
        Assert.Contains(recipe.Inputs, i => i.ItemId == "fibers" && i.Quantity == 1);
    }

    [Fact]
    public void PlayerBaitedCast_DoublesCatchAndConsumesBait()
    {
        var system = new ActionSystem();
        var node = FishSpot();
        var sm = new SkillManager();
        PumpSuccessRate(sm, 1000f);
        var inv = new Inv();
        Assert.True(inv.AddItem("fishing_rod", 1));
        Assert.True(inv.AddItem("fishing_bait", 3));

        Assert.Null(system.StartAction(ActionType.Fishing, node, sm, inv));

        // Bait is consumed the moment the cast starts (it sank with the
        // cast) — before the window closes.
        Assert.Equal(2, inv.GetItemQuantity("fishing_bait"));
        Assert.True(system.Active.Baited);

        // Wait out the cast window.
        float remaining = Constants.FishingCastSeconds;
        while (remaining > 0.25f)
        {
            Assert.Null(system.Update(0.25f));
            remaining -= 0.25f;
        }
        var result = system.Update(0.25f);
        Assert.NotNull(result);
        Assert.True(result!.Success);
        // Doubled catch: 1 base yield × 2 (bait).
        Assert.Equal(2, result.Quantity);
        Assert.Contains("(bait)", result.Message);
        Assert.Equal(15f, result.Xp); // XP is per-catch, not per-fish
        // 3 bait − 1 consumed at cast start = 2 remain; consumption happens
        // once per cast, not per fish.
        Assert.Equal(2, inv.GetItemQuantity("fishing_bait"));
    }

    [Fact]
    public void PlayerBaitedCast_CancelGivesNoBaitRefund()
    {
        var system = new ActionSystem();
        var node = FishSpot();
        var sm = new SkillManager();
        var inv = new Inv();
        Assert.True(inv.AddItem("fishing_rod", 1));
        Assert.True(inv.AddItem("fishing_bait", 2));

        Assert.Null(system.StartAction(ActionType.Fishing, node, sm, inv));
        Assert.True(system.Active.Baited);

        // Walk away mid-cast: the cast cancels, the bait is gone (it sank).
        Assert.True(system.CancelActive());
        Assert.Equal(1, inv.GetItemQuantity("fishing_bait"));

        // A fresh cast with the last bait works normally.
        Assert.Null(system.StartAction(ActionType.Fishing, node, sm, inv));
        Assert.Equal(0, inv.GetItemQuantity("fishing_bait"));
    }

    [Fact]
    public void WorkerBaitedHarvest_BurnsColonyBaitAndDoubles()
    {
        var h = new Harness();
        h.FoundColony(5, 5);
        FishSpotAt(h.World, 6, 6);
        h.Colony.Store("fishing_rod", 1);
        h.Colony.Store("fishing_bait", 5);
        var worker = h.AddWorker("fisher", 5, 6);

        h.Tick(60); // several harvest rounds

        // Bait was burned from the colony store…
        Assert.True(h.Colony.GetItemQuantity("fishing_bait") < 5,
            $"expected bait consumed, got {h.Colony.GetItemQuantity("fishing_bait")}");
        // …and the haul landed doubled (more fish than a no-bait run of the
        // same length could produce: depletion_count 2 caps an unbaited
        // node at 2 fish ever; baited doubles per harvest).
        Assert.True(h.Colony.GetItemQuantity("raw_fish") >= 2,
            $"expected doubled haul in store, got {h.Colony.GetItemQuantity("raw_fish")}");
    }

    // ─── Rod tiers: bone rod — faster cast, store-arm fix ────────────────

    [Fact]
    public void BoneRodData_ItemAndRecipe_LoadFromJson()
    {
        var items = DataLoader.LoadJsonList<ItemDef>(Constants.ItemsFile, "items");
        var rod = items.FirstOrDefault(i => i.Id == "bone_fishing_rod");
        Assert.NotNull(rod);
        Assert.Equal("fishing_rod", rod!.ToolType);
        Assert.True(rod.IsEquippable);
        Assert.True(rod.IsEssentialTool);
        Assert.Equal(100, rod.Durability);
        Assert.Equal("items/bone_wolf", rod.SpriteKey);

        var recipes = new RecipeRegistry();
        recipes.LoadAll();
        var recipe = recipes.GetRecipe("craft_bone_fishing_rod");
        Assert.NotNull(recipe);
        Assert.Equal("bone_fishing_rod", recipe!.OutputItem);
        Assert.Equal("crafting", recipe.RequiredSkill);
        Assert.Equal(4, recipe.RequiredLevel);
        Assert.Contains(recipe.Inputs, i => i.ItemId == "wolf_bone" && i.Quantity == 2);
        Assert.Contains(recipe.Inputs, i => i.ItemId == "grass_rope" && i.Quantity == 1);
    }

    [Fact]
    public void BoneRod_PassesGateAndCastsFaster()
    {
        var system = new ActionSystem();
        var node = FishSpot();
        var sm = new SkillManager();
        PumpSuccessRate(sm, 1000f);
        var inv = new Inv();
        // ONLY the bone rod — proves the suffix match recognizes it as a
        // fishing_rod with no base rod present.
        Assert.True(inv.AddItem("bone_fishing_rod", 1));

        Assert.Null(system.StartAction(ActionType.Fishing, node, sm, inv));
        Assert.True(system.Active.IsBusy);

        // The bone rod's cast is 2s, not 3s: at 2.25s of Updates the catch
        // has already fired (a base-rod cast would still be holding).
        float remaining = Constants.BoneFishingCastSeconds;
        while (remaining > 0.25f)
        {
            Assert.Null(system.Update(0.25f));
            remaining -= 0.25f;
        }
        var result = system.Update(0.25f);
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("raw_fish", result.ItemId);
        Assert.Equal(1, result.Quantity); // tier buys speed, not yield
        Assert.Equal(15f, result.Xp);

        // A base-rod regression lives in the existing 3s tests
        // (MidCastIsBusyAndHoldsSilence pins 2.9s-still-busy).
    }

    [Fact]
    public void WorkerStore_BoneRod_SatisfiesTheGate()
    {
        var h = new Harness();
        h.FoundColony(5, 5);
        FishSpotAt(h.World, 6, 6);
        // ONLY a bone rod in the store — no plain fishing_rod. Proves the
        // bone_ store arm (exact + stone_ match would leave the node
        // unclaimed and nothing carried).
        h.Colony.Store("bone_fishing_rod", 1);
        var worker = h.AddWorker("fisher", 5, 6);

        h.Tick(40);

        Assert.True(h.Colony.GetItemQuantity("raw_fish") > 0,
            $"expected raw_fish from bone-rod store gate, got {h.Colony.GetItemQuantity("raw_fish")}");
        Assert.True(worker.Skills.GetSkill("fishing").Xp > 0f,
            "bone-rod fisher still trains fishing");
    }

    // ─── Rare drops: pearl + old boot, level-scaled ─────────────────────

    [Fact]
    public void RareDropData_ItemsAndTradeEntry_LoadFromJson()
    {
        var items = DataLoader.LoadJsonList<ItemDef>(Constants.ItemsFile, "items");
        var pearl = items.FirstOrDefault(i => i.Id == "pearl");
        Assert.NotNull(pearl);
        Assert.Equal(5, pearl!.StackSize);
        Assert.False(pearl.IsEquippable);
        Assert.False(pearl.IsEssentialTool);
        Assert.False(pearl.IsFood);
        Assert.Equal("items/moonstone", pearl.SpriteKey);

        var boot = items.FirstOrDefault(i => i.Id == "old_boot");
        Assert.NotNull(boot);
        Assert.Equal(5, boot!.StackSize);
        Assert.False(boot.IsEquippable);
        Assert.False(boot.IsEssentialTool);
        Assert.False(boot.IsFood);
        Assert.Equal("gear/leather_boots", boot.SpriteKey);

        var trade = new TradeItemRegistry();
        trade.LoadAll();
        var entry = trade.GetTradeItem("coastal_pearl");
        Assert.NotNull(entry);
        Assert.Equal("pearl", entry!.ItemId);
        Assert.Equal("coastal", entry.Biome);
        Assert.Equal(25, entry.SellPrice);
        Assert.Equal(60, entry.BuyPrice);
        Assert.Equal(0, entry.StockQuantity);
        Assert.Equal(0, entry.MaxStock);
    }

    [Fact]
    public void RareTable_Roll_LevelsScaleTheWindows()
    {
        // Level 1: pearl window [0, 0.01), boot window [0.01, 0.05).
        Assert.Equal("pearl", FishingRareTable.Roll(0.005f, 1));
        Assert.Equal("old_boot", FishingRareTable.Roll(0.02f, 1));
        Assert.Null(FishingRareTable.Roll(0.06f, 1));

        // Level 30: pearl window grows to [0, 0.0535) — the same roll that
        // said "boot" at level 1 now says "pearl".
        Assert.Equal("pearl", FishingRareTable.Roll(0.03f, 30));
        Assert.Null(FishingRareTable.Roll(0.999f, 99));

        // Level 0 (unregistered skill fallback) clamps to level 1.
        Assert.Equal("pearl", FishingRareTable.Roll(0.005f, 0));
    }

    [Fact]
    public void PlayerRareCatch_ForcedPearl_LandsInInventoryWithMessage()
    {
        var system = new ActionSystem { ForceRareDrop = "pearl" };
        var node = FishSpot();
        var sm = new SkillManager();
        PumpSuccessRate(sm, 1000f);
        var inv = new Inv();
        Assert.True(inv.AddItem("fishing_rod", 1));

        Assert.Null(system.StartAction(ActionType.Fishing, node, sm, inv));
        float remaining = Constants.FishingCastSeconds;
        while (remaining > 0.25f)
        {
            Assert.Null(system.Update(0.25f));
            remaining -= 0.25f;
        }
        var result = system.Update(0.25f);
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("pearl", result.RareItemId);
        Assert.Contains("dredged up a pearl", result.Message);

        system.ProcessCompletion(result, inv, sm);
        Assert.Equal(1, inv.GetItemQuantity("pearl"));
        // The rare grant posts the gold rare-find notification.
        var notifications = system.FlushNotifications();
        Assert.Contains(notifications, n =>
            n.Text.Contains("+1 pearl") && n.Color.R == 255 && n.Color.G == 215);
    }

    [Fact]
    public void PlayerRareCatch_ForcedOldBoot_StillJunk()
    {
        var system = new ActionSystem { ForceRareDrop = "old_boot" };
        var node = FishSpot();
        var sm = new SkillManager();
        PumpSuccessRate(sm, 1000f);
        var inv = new Inv();
        Assert.True(inv.AddItem("fishing_rod", 1));

        Assert.Null(system.StartAction(ActionType.Fishing, node, sm, inv));
        float remaining = Constants.FishingCastSeconds;
        while (remaining > 0.25f)
        {
            Assert.Null(system.Update(0.25f));
            remaining -= 0.25f;
        }
        var result = system.Update(0.25f);
        Assert.NotNull(result);
        Assert.Equal("old_boot", result!.RareItemId);
        Assert.Contains("dredged up a old_boot", result.Message);

        system.ProcessCompletion(result, inv, sm);
        Assert.Equal(1, inv.GetItemQuantity("old_boot"));
    }

    [Fact]
    public void PlayerCast_StashesFishingLevelAtCastStart()
    {
        var system = new ActionSystem();
        var node = FishSpot();
        var sm = new SkillManager();
        var inv = new Inv();
        Assert.True(inv.AddItem("fishing_rod", 1));

        // A fresh fisher casts at level 1…
        Assert.Null(system.StartAction(ActionType.Fishing, node, sm, inv));
        Assert.Equal(1, system.Active.FishingLevel);

        // A levelled fisher casts at their level: pump to level 5.
        sm.AddXpWithNotification("fishing", SkillManager.XpForLevel(5) + 1f);
        Assert.Equal(5, sm.GetSkillLevel("fishing"));
        system.CancelActive();
        Assert.Null(system.StartAction(ActionType.Fishing, node, sm, inv));
        Assert.Equal(5, system.Active.FishingLevel);
    }

    [Fact]
    public void PlayerCast_ForcedNoDrop_KeepsPlainCatchShape()
    {
        var system = new ActionSystem { ForceRareDrop = "" };
        var node = FishSpot();
        var sm = new SkillManager();
        PumpSuccessRate(sm, 1000f);
        var inv = new Inv();
        Assert.True(inv.AddItem("fishing_rod", 1));

        Assert.Null(system.StartAction(ActionType.Fishing, node, sm, inv));
        float remaining = Constants.FishingCastSeconds;
        while (remaining > 0.25f)
        {
            Assert.Null(system.Update(0.25f));
            remaining -= 0.25f;
        }
        var result = system.Update(0.25f);
        Assert.NotNull(result);
        Assert.Null(result!.RareItemId);
        // Message shape unchanged: no dredged clause.
        Assert.Equal("Harvested 1 raw_fish.", result.Message);
    }

    [Fact]
    public void PlayerRareCatch_FullInventory_VisibleLoss()
    {
        var system = new ActionSystem { ForceRareDrop = "pearl" };
        var node = FishSpot();
        var sm = new SkillManager();
        PumpSuccessRate(sm, 1000f);
        var inv = new Inv();
        Assert.True(inv.AddItem("fishing_rod", 1));

        // Fill every slot: the rod already holds one slot; 19 distinct
        // filler ids take the rest (GetStackSize's fallback default is a
        // stack of 10 — same-id fillers would merge, distinct ids can't).
        for (int i = 0; i < 19; i++)
            Assert.True(inv.AddItem($"rod_filler_{i}", 5));
        Assert.False(inv.CanAdd("raw_fish", 1));

        Assert.Null(system.StartAction(ActionType.Fishing, node, sm, inv));
        float remaining = Constants.FishingCastSeconds;
        while (remaining > 0.25f)
        {
            Assert.Null(system.Update(0.25f));
            remaining -= 0.25f;
        }
        var result = system.Update(0.25f);
        Assert.NotNull(result);
        Assert.Equal("pearl", result!.RareItemId);

        system.ProcessCompletion(result, inv, sm);
        // The pearl is lost — 21 rods don't hide it — but VISIBLY.
        Assert.Equal(0, inv.GetItemQuantity("pearl"));
        var notifications = system.FlushNotifications();
        Assert.Contains(notifications, n =>
            n.Text.Contains("pearl") && n.Text.Contains("slipped back into the water")
            && n.Color.R == 255 && n.Color.G == 100 && n.Color.B == 100);
    }

    [Fact]
    public void WorkerRareCatch_ForcedPearl_LandsInColonyStore()
    {
        var h = new Harness();
        h.FoundColony(5, 5);
        FishSpotAt(h.World, 6, 6);
        h.Colony.Store("fishing_rod", 1);
        h.Recruits.ForceRareDrop = "pearl";
        var worker = h.AddWorker("fisher", 5, 6);

        h.Tick(60);

        // The rare lands in the stockpile ledger alongside the fish.
        Assert.True(h.Colony.GetItemQuantity("pearl") >= 1,
            $"expected pearl in store, got {h.Colony.GetItemQuantity("pearl")}");
        Assert.True(h.Colony.GetItemQuantity("raw_fish") > 0);
        // The worker's haul state stays single-type cargo (pearl isn't cargo).
        Assert.NotEqual("pearl", worker.CarriedItemId);
    }

    [Fact]
    public void WorkerRareCatch_PickaxeNodes_NeverRoll()
    {
        var h = new Harness();
        h.FoundColony(5, 5);
        var copper = new ResourceDef
        {
            Id = "copper_rock",
            Name = "Copper Rock",
            YieldItem = "copper_ore",
            Yield = 1,
            RequiredLevel = 1,
            ToolRequirement = "pickaxe",
            Xp = 35f,
            DepletionCount = 5,
            Seasons = [],
        };
        h.World.GetTile(6, 6)!.ResourceNode =
            new ResourceNode("copper_rock", copper, 10f);
        h.Colony.Store("pickaxe", 1);
        h.Recruits.ForceRareDrop = "pearl";
        var miner = h.AddWorker("miner", 6, 7);

        h.Tick(60);

        // Ore mined, XP trained — but the rare table never fires on a
        // non-rod node.
        Assert.True(h.Colony.GetItemQuantity("copper_ore") > 0);
        Assert.Equal(0, h.Colony.GetItemQuantity("pearl"));
    }

    [Fact]
    public void PearlTrade_SellOnly_NoMerchantStock()
    {
        // Registry loads the real trade_items.json.
        var registry = new TradeItemRegistry();
        registry.LoadAll();
        var trade = new TradeSystem { Registry = registry };
        var merchant = new MerchantNpc
        {
            NpcId = "coastal_merchant",
            Name = "Coastal Merchant",
            Biome = "coastal",
            FactionId = "",
            PriceModifier = 1f,
            StartingGold = 1000,
        };

        // The pearl has a market price…
        Assert.Equal(25, trade.SellPriceFor("pearl", merchant));

        // …but no stock to buy back — the payday is sell-only.
        var result = trade.ExecuteBuy("coastal_pearl", 1, merchant, new Inv(), new SkillManager());
        Assert.False(result.Success);
        Assert.Contains("Out of stock", result.Message);

        // The old boot has no market at all — junk stays junk.
        Assert.Equal(0, trade.SellPriceFor("old_boot", merchant));
    }
}
