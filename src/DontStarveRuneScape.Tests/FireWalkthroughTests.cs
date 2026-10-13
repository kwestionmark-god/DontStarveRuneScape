namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.World;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>
/// Vertical building, slice 19: WALKING THROUGH a lit fire bites on
/// ENTRY — the slice-12 standing damage only pays off after a full
/// second, so sprinting across a campfire was free. Entry bite =
/// ceil(ContactDamage / 2), once per entry (leaving resets the latch).
/// </summary>
public class FireWalkthroughTests
{
    private sealed class Harness
    {
        public TileMap World = new(8, 8);
        public BuildingSystem Buildings = MakeBuildings();
        public SkillManager Skills = new();
        public Inv Inventory = new();
        public Skills.Firemaking.FiremakingSkill Firemaking = new();

        public Harness()
        {
            for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
                World.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
            World.BuildCornerElevations();
            Inventory.AddItem("stick", 30);
            Inventory.AddItem("stone", 30);
            Inventory.AddItem("oak_logs", 10);
            Skills.AddXp("construction", 500f);
        }

        private static BuildingSystem MakeBuildings()
        {
            var registry = new StructureDefRegistry();
            registry.LoadAll();
            return new BuildingSystem { Registry = registry };
        }

        public Player PlayerAt(float wx, float wy)
            => new Player(wx, wy)
            {
                Inventory = Inventory, SkillManager = Skills,
                Survival = new Survival.SurvivalSystem(),
                Gear = new PlayerGear(), ActionSystem = new ActionSystem(),
            };

        /// <summary>Place + light the structure at tile (1,1) sub (1,1).</summary>
        public Structure LitHearth(string id)
        {
            var placed = Buildings.PlaceStructure(id, 1, 1, 1, 1,
                World, Inventory, Skills);
            Assert.True(placed.Success, placed.Message);
            var s = Buildings.Structures.Single(x => x.StructureId == id);
            Firemaking.LightHearth(s.StructureDef!.BurnSeconds, s.WorldX, s.WorldY);
            return s;
        }

        /// <summary>Player centered on a covered sub-cell.</summary>
        public float CenterX(Structure s)
            => (s.TileX * Constants.BrickSubCells + s.SubX + 1) * Constants.BrickCellSize + 8f;

        public float CenterY(Structure s)
            => (s.TileY * Constants.BrickSubCells + s.SubY + 1) * Constants.BrickCellSize + 8f;
    }

    [Fact]
    public void CrossingALitCampfire_BitesOnEntry()
    {
        var h = new Harness();
        var s = h.LitHearth("campfire");
        var player = h.PlayerAt(h.CenterX(s) - 40f, h.CenterY(s)); // off the footprint
        float hp0 = player.Survival!.Hp;

        // One short frame inside the footprint — far under the 1s
        // standing-drain tick.
        player.WorldX = h.CenterX(s);
        h.Firemaking.TickContactDamage(player, h.Buildings, 0.1f);

        Assert.True(player.Survival.Hp <= hp0 - 1f,
            $"expected an entry bite, hp {hp0} -> {player.Survival.Hp}");
    }

    [Fact]
    public void StandingInside_StillAccumulatesPerSecond()
    {
        var h = new Harness();
        var s = h.LitHearth("campfire");
        var player = h.PlayerAt(h.CenterX(s), h.CenterY(s));
        float hp0 = player.Survival!.Hp;

        for (int i = 0; i < 10; i++) // 1.0s of standing
            h.Firemaking.TickContactDamage(player, h.Buildings, 0.1f);

        // Contact 2/s: entry bite 1 (paid on frame 1) then the accumulator
        // pays 2 whole HP over the second — at least 2 total by 1s.
        Assert.True(player.Survival.Hp <= hp0 - 2f, $"hp {hp0} -> {player.Survival.Hp}");
    }

    [Fact]
    public void LeavingAndReturning_BitesAgain()
    {
        var h = new Harness();
        var s = h.LitHearth("campfire");
        var player = h.PlayerAt(h.CenterX(s), h.CenterY(s));
        float hp0 = player.Survival!.Hp;

        h.Firemaking.TickContactDamage(player, h.Buildings, 0.1f); // entry
        player.WorldX = h.CenterX(s) - 40f;                        // step off
        h.Firemaking.TickContactDamage(player, h.Buildings, 0.1f); // latch resets
        player.WorldX = h.CenterX(s);                              // step back in
        h.Firemaking.TickContactDamage(player, h.Buildings, 0.1f); // entry 2

        Assert.True(player.Survival.Hp <= hp0 - 2f, $"hp {hp0} -> {player.Survival.Hp}");
    }

    [Fact]
    public void CrossingALinedPit_NeverBites()
    {
        var h = new Harness();
        var s = h.LitHearth("lined_fire_pit");
        var player = h.PlayerAt(h.CenterX(s), h.CenterY(s));
        float hp0 = player.Survival!.Hp;
        h.Firemaking.TickContactDamage(player, h.Buildings, 0.1f);
        Assert.Equal(hp0, player.Survival.Hp);
    }

    [Fact]
    public void EntryBiteScalesWithContactDamage()
    {
        // ceil(2/2)=1 for the campfire; ceil(3/2)=2 for the bonfire.
        var h = new Harness();
        var s = h.LitHearth("bonfire");
        var player = h.PlayerAt(h.CenterX(s), h.CenterY(s));
        float hp0 = player.Survival!.Hp;
        h.Firemaking.TickContactDamage(player, h.Buildings, 0.1f);
        Assert.True(player.Survival.Hp <= hp0 - 2f, $"bonfire entry bite, hp {hp0} -> {player.Survival.Hp}");
    }
}
