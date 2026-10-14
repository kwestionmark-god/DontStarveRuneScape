namespace DontStarveRuneScape.Tests;

using System.Linq;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.World;
using Xunit;

/// <summary>
/// Cave food pressure (Stage 3 follow-up): the cave has NO food today, so
/// a deep expedition is a strictly one-way hunger timer (a 2026-10-13
/// in-cave capture showed Hunger 0/100, HP 6/20). The fix is pressure with
/// a lifeline: glowcap mushrooms grow in dim rings at mid-depth — a raw,
/// modest, slightly dubious food that keeps a desperate run alive without
/// replacing real surface rations. Placement is deterministic per seed,
/// like the ore veins.
/// </summary>
public class CaveFoodTests
{
    [Fact]
    public void GlowcapClusters_ArePlacedAtMidDepth()
    {
        var cave = CaveWorldSystem.Generate(12345, CaveInnardsTests.RealResources(), null);

        var caps = AllTiles(cave)
            .Select((t, i) => (Tile: t, X: i % cave.Width, Y: i / cave.Width))
            .Where(c => c.Tile.ResourceNode?.ResourceId == "glow_cap")
            .ToList();

        // A handful of small rings — enough to matter, scarce enough to
        // keep the pressure on. No caps right at the exit mouth.
        Assert.InRange(caps.Count, 8, 30);
        Assert.All(caps, c =>
        {
            Assert.False(c.Tile.IsCaveExit);
            Assert.InRange(c.X, 14, 55);            // mid-depth band, off the exit mouth
            Assert.Equal("plants/glow_cap", c.Tile.ResourceNode!.ResourceDef!.SpriteKey);
            Assert.Equal(2, c.Tile.ResourceNode!.ResourceDef!.DepletionCount);
        });
    }

    [Fact]
    public void GlowcapClusters_AreDeterministicPerSeed()
    {
        var a = CaveWorldSystem.Generate(12345, CaveInnardsTests.RealResources(), null);
        var b = CaveWorldSystem.Generate(12345, CaveInnardsTests.RealResources(), null);
        Assert.Equal(CapSpots(a), CapSpots(b));
    }

    [Fact]
    public void Glowcap_IsAForageableRawFood()
    {
        var loader = new DataLoader();
        loader.LoadAll();

        // Item side: edible, raw, a modest desperation snack — well under
        // real surface food so rations still win.
        var foods = new FoodRegistry(loader.ItemsData);
        var cap = foods.Get("glow_cap");
        Assert.NotNull(cap);
        Assert.True(cap.HungerRestoration is >= 6f and <= 12f,
            $"glowcap hunger restore {cap.HungerRestoration} should sit in the 6-12 desperation band");
        Assert.True(cap.IsRaw);

        // Resource side: a herb you can pull by hand — no pickaxe gate.
        var row = loader.ResourcesData.First(d =>
            (d.TryGetValue("id", out var id) ? id?.ToString() : null) == "glow_cap");
        Assert.Equal("herb", row["category"]?.ToString());
        Assert.True(row["requires_tool"] is null || row["requires_tool"].ToString() == "");
    }

    private static List<(int X, int Y)> CapSpots(TileMap map)
        => AllTiles(map)
            .Select((t, i) => (Tile: t, X: i % map.Width, Y: i / map.Width))
            .Where(c => c.Tile.ResourceNode?.ResourceId == "glow_cap")
            .Select(c => (c.X, c.Y))
            .ToList();

    private static IEnumerable<Tile> AllTiles(TileMap map)
    {
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
            yield return map.Tiles[x, y];
    }
}
