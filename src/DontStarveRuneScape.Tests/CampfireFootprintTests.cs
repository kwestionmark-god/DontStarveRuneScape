namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Skills.Firemaking;
using DontStarveRuneScape.World;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>
/// Vertical building, slice 12 (spec:
/// docs/superpowers/specs/2026-10-12-campfire-footprint-design.md):
/// the campfire is the first multi-sub-cell structure — a 2x2 footprint
/// solid on the 4x4 grid (0.5 units squat: grounded movers are walled,
/// the player CAN jump on), standing on a LIT one bites (2 HP/s),
/// and the Stone-Lined Fire Pit variant trades cost (stone 6 + stick 2,
/// construction 2) for safety (zero contact damage) and efficiency
/// (burn_rate 1.5 -> 135 s). Burn rate is a def field feeding every
/// light call site (BurnSeconds). Legacy tile-level campfire rows
/// migrate to the centered footprint — NOT into the mound brick
/// blow-up. User decisions 2026-10-12 (all recommended defaults).
/// </summary>
public class CampfireFootprintTests
{
    private sealed class Harness
    {
        public TileMap World = new TileMap(8, 8);
        public BuildingSystem Buildings = MakeBuildings();
        public SkillManager Skills = new();
        public Inv Inventory = new();
        public FiremakingSkill Firemaking = new();

        public Harness()
        {
            for (int x = 0; x < 8; x++)
                for (int y = 0; y < 8; y++)
                    World.Tiles[x, y].Elevation = Constants.SeaLevel + 5f; // 16.5
            World.BuildCornerElevations();
            Inventory.AddItem("dirt_brick", 64);
            Inventory.AddItem("stick", 30);
            Inventory.AddItem("stone", 30);
            Inventory.AddItem("planks", 8);
            // A pit-builder has construction XP (the lined pit gates at
            // level 2 = 83 XP on the OSRS curve).
            Skills.AddXp("construction", 100f);
        }

        private static BuildingSystem MakeBuildings()
        {
            var registry = new StructureDefRegistry();
            registry.LoadAll();
            return new BuildingSystem { Registry = registry };
        }

        public (bool Success, string Message) PlaceBrick(int tx, int ty, int subX, int subY)
            => Buildings.PlaceStructure("mound_block", tx, ty, subX, subY,
                World, Inventory, Skills);

        public (bool Success, string Message) Place(string id, int tx, int ty)
            => Buildings.PlaceStructure(id, tx, ty, World, Inventory, Skills);

        public (bool Success, string Message) PlaceAt(string id, int tx, int ty, int subX, int subY)
            => Buildings.PlaceStructure(id, tx, ty, subX, subY, World, Inventory, Skills);

        public (bool Success, string Message) PlaceLit(string id, int tx, int ty)
            => Buildings.PlaceStructure(id, tx, ty, World, Inventory, Skills, null, Firemaking);
    }

    private static Player PlayerAt(float wx, float wy)
        => new Player(wx, wy)
        {
            Inventory = new Inv(),
            SkillManager = new SkillManager(),
            Survival = new Survival.SurvivalSystem(),
            Gear = new PlayerGear(),
            ActionSystem = new ActionSystem(),
        };

    // ── 1. Data pins ───────────────────────────────────────────────────────

    [Fact]
    public void CampfireDef_IsTheFootprintBlock()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();

        var camp = registry.GetStructure("campfire");
        Assert.NotNull(camp);
        Assert.True(camp!.IsBlock, "the campfire is a squat solid on the brick grid");
        Assert.Equal(0.5f, camp.StackHeight);
        Assert.Equal(2, camp.Footprint);
        Assert.True(camp.OccupiesTile);
        Assert.Equal(2f, camp.ContactDamage);
        Assert.Equal(1f, camp.BurnRate);
        Assert.Equal(90f, camp.BurnSeconds);
        Assert.Equal("structure/campfire", camp.SpriteKey);

        // The authored texture ships with the build and is 32x32 — the
        // 2x2 footprint face at literal 1:1 (the slice-8 full-bleed law).
        const string path = "Assets/sprites/structure/campfire.png";
        Assert.True(File.Exists(path), $"expected campfire texture at {path}");
        var bytes = File.ReadAllBytes(path);
        int w = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        int hgt = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        Assert.Equal(32, w);
        Assert.Equal(32, hgt);
    }

    [Fact]
    public void LinedFirePitDef_IsTheSafeEfficientVariant()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();

        var lined = registry.GetStructure("lined_fire_pit");
        Assert.NotNull(lined);
        Assert.True(lined!.IsBlock);
        Assert.Equal(0.5f, lined.StackHeight);
        Assert.Equal(2, lined.Footprint);
        Assert.True(lined.OccupiesTile);
        Assert.Equal(0f, lined.ContactDamage);      // safe to stand on
        Assert.Equal(1.5f, lined.BurnRate);          // efficient burn
        Assert.Equal(135f, lined.BurnSeconds);       // 90 x 1.5
        Assert.Equal(2, lined.RequiresSkillLevel);   // construction 2
        Assert.Equal("structure/stone_fire_ring", lined.SpriteKey);
        Assert.Contains(lined.Materials, m => m.ItemId == "stone" && m.Quantity == 6);
        Assert.Contains(lined.Materials, m => m.ItemId == "stick" && m.Quantity == 2);
    }

    // ── 2. Footprint placement + solidity ─────────────────────────────────

    [Fact]
    public void Campfire_PlacesAtAnchor_2x2Solid()
    {
        var h = new Harness();
        var placed = h.PlaceAt("campfire", 4, 4, 1, 1);
        Assert.True(placed.Success, placed.Message);

        var s = h.Buildings.Structures.Single(x => x.StructureId == "campfire");
        Assert.Equal(1, s.SubX);
        Assert.Equal(1, s.SubY);
        Assert.Equal(4 * Constants.TileSize + 2 * Constants.BrickCellSize,
            s.WorldX); // the 2x2 center = the tile center

        // Solid on exactly the four covered cells, nothing elsewhere.
        Assert.Equal(0.5f, BlockStack.SubHeightAt(h.World, h.Buildings, 4, 4, 1, 1));
        Assert.Equal(0.5f, BlockStack.SubHeightAt(h.World, h.Buildings, 4, 4, 2, 1));
        Assert.Equal(0.5f, BlockStack.SubHeightAt(h.World, h.Buildings, 4, 4, 1, 2));
        Assert.Equal(0.5f, BlockStack.SubHeightAt(h.World, h.Buildings, 4, 4, 2, 2));
        Assert.Equal(0f, BlockStack.SubHeightAt(h.World, h.Buildings, 4, 4, 0, 0));
        Assert.Equal(0f, BlockStack.SubHeightAt(h.World, h.Buildings, 4, 4, 3, 3));
    }

    [Fact]
    public void Campfire_TileLevelCall_AutoCenters()
    {
        // Every existing id-based call site (tests, saves, panels) keeps
        // working: the tile-level call centers the footprint.
        var h = new Harness();
        var placed = h.Place("campfire", 4, 4);
        Assert.True(placed.Success, placed.Message);

        var s = h.Buildings.Structures.Single(x => x.StructureId == "campfire");
        Assert.Equal(1, s.SubX);
        Assert.Equal(1, s.SubY);
    }

    // ── 3. Collision: walls grounded movers, admits the player's jump ─────

    [Fact]
    public void Campfire_GroundedMover_Blocked_AirborneHopAllowed()
    {
        var h = new Harness();
        Assert.True(h.Place("campfire", 4, 4).Success);

        // A grounded step into the footprint (sub (1,2) cell) is a wall…
        var (dx, dy) = BlockStack.GroundedSlide(h.World, h.Buildings,
            265f, 288f, 12f, 0f);
        Assert.Equal(0f, dx);
        Assert.Equal(0f, dy);

        // …and an airborne hop clears it (0.5 <= BlockClimbCap 2.05) — the
        // player can jump ONTO the fire, which is where contact damage
        // bites.
        Assert.True(BlockStack.CanStep(h.World, h.Buildings,
            265f, 288f, 277f, 288f, airborne: true));
    }

    [Fact]
    public void Campfire_SpillingAnchor_Refused()
    {
        var h = new Harness();
        var placed = h.PlaceAt("campfire", 4, 4, 3, 3); // 2x2 spills the tile

        Assert.False(placed.Success);
        Assert.Equal("The footprint doesn't fit there.", placed.Message);
    }

    // ── 4. Occupancy: bricks and footprints share a tile honestly ──────────

    [Fact]
    public void Brick_FootprintCoexistence()
    {
        var h = new Harness();
        Assert.True(h.Place("campfire", 4, 4).Success);

        // A free sub-cell of the campfire tile takes a brick…
        var free = h.PlaceBrick(4, 4, 0, 0);
        Assert.True(free.Success, free.Message);

        // …an overlapping cell refuses…
        var overlap = h.PlaceBrick(4, 4, 2, 2);
        Assert.False(overlap.Success);

        // …and removing the campfire frees its cells live.
        var camp = h.Buildings.Structures.Single(x => x.StructureId == "campfire");
        Assert.True(h.Buildings.RemoveStructure(camp, h.World));
        var freed = h.PlaceBrick(4, 4, 2, 2);
        Assert.True(freed.Success, freed.Message);
    }

    // ── 5. Burn rate feeds every light ─────────────────────────────────────

    [Fact]
    public void Placement_LightsBurnSeconds()
    {
        var h = new Harness();
        var camp = h.PlaceLit("campfire", 4, 4);
        Assert.True(camp.Success, camp.Message);
        Assert.Equal(90f, Assert.Single(h.Firemaking.GetActiveFires()).RemainingTime);

        var lined = h.PlaceLit("lined_fire_pit", 6, 4);
        Assert.True(lined.Success, lined.Message);
        Assert.Equal(135f, h.Firemaking.GetActiveFires().Last().RemainingTime);
    }

    // ── 6. Standing on a lit fire bites; lined variants never do ───────────

    [Fact]
    public void TickContactDamage_LitFireOnly_BitesPlayer()
    {
        var h = new Harness();
        Assert.True(h.Place("campfire", 4, 4).Success);
        var s = h.Buildings.Structures.Single(x => x.StructureId == "campfire");
        h.Firemaking.LightHearth(s.StructureDef!.BurnSeconds, s.WorldX, s.WorldY);

        var player = PlayerAt(s.WorldX, s.WorldY); // standing on the footprint
        float hp0 = player.Survival!.Hp;
        for (int i = 0; i < 12; i++) // 3 s at 2 HP/s = ~6 HP
            h.Firemaking.TickContactDamage(player, h.Buildings, 0.25f);

        Assert.True(player.Survival.Hp < hp0,
            "standing on a lit campfire must bite");
        player.ActionSystem!.FlushNotifications();
        Assert.Contains(player.ActionSystem.Notifications,
            n => n.Text.Contains("fire", StringComparison.OrdinalIgnoreCase));

        // Unlit: the burned-out ring is safe to stand on.
        var idle = PlayerAt(s.WorldX, s.WorldY);
        float hpIdle = idle.Survival!.Hp;
        h.Firemaking = new FiremakingSkill(); // no live fire
        for (int i = 0; i < 12; i++)
            h.Firemaking.TickContactDamage(idle, h.Buildings, 0.25f);
        Assert.Equal(hpIdle, idle.Survival.Hp);
    }

    [Fact]
    public void TickContactDamage_LinedPit_IsSafe()
    {
        var h = new Harness();
        var lined = h.PlaceLit("lined_fire_pit", 4, 4);
        Assert.True(lined.Success, lined.Message);
        var s = h.Buildings.Structures.Single(x => x.StructureId == "lined_fire_pit");

        var player = PlayerAt(s.WorldX, s.WorldY);
        float hp0 = player.Survival!.Hp;
        for (int i = 0; i < 16; i++)
            h.Firemaking.TickContactDamage(player, h.Buildings, 0.25f);

        Assert.Equal(hp0, player.Survival!.Hp);
    }

    // ── 7. Legacy save rows migrate to the centered footprint ──────────────

    [Fact]
    public void LegacyCampfireRow_MigratesToCenteredFootprint()
    {
        var h = new Harness();
        var snapshot = new BuildingSnapshot
        {
            Structures =
            [
                new StructureSnapshot
                {
                    StructureId = "campfire",
                    TileX = 4,
                    TileY = 4,
                    SubX = -1,          // the pre-footprint tile-level row
                    SubY = -1,
                    Hp = 5,
                },
            ],
        };

        h.Buildings.RestoreSnapshot(snapshot, h.World);

        var restored = h.Buildings.Structures.Where(x => x.StructureId == "campfire").ToList();
        var camp = Assert.Single(restored); // NOT 32 copies (the mound blow-up must not fire)
        Assert.Equal(1, camp.SubX);
        Assert.Equal(1, camp.SubY);
        Assert.True(camp.IsActive);
        // Live collision: the four covered cells are solid.
        Assert.Equal(0.5f, BlockStack.SubHeightAt(h.World, h.Buildings, 4, 4, 1, 1));
        Assert.Equal(0.5f, BlockStack.SubHeightAt(h.World, h.Buildings, 4, 4, 2, 2));
    }
}
