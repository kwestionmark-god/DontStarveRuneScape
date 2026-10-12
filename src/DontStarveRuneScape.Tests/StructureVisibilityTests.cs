namespace DontStarveRuneScape.Tests;

using System;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Render;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.World;
using GameCamera = DontStarveRuneScape.Camera.Camera;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>
/// Vertical building, slice 10 (spec:
/// docs/superpowers/specs/2026-10-12-structure-sort-honesty-design.md):
/// a structure never loses the painter's-order tie with its own terrain
/// tile. User critique (2026-10-12): bricks "sink/bury into the ground or
/// vanish at some angles, then come back as I keep panning" — the yaw
/// orbit flips the sub-cell position offset to the far side of the view
/// axis and the owning tile quad paints over the build. The fix is the
/// entity law the player/monsters already follow (SortElevation + the
/// standing boost); genuine occlusion (raised tiles between the camera
/// and the build) must keep winning.
/// </summary>
public class StructureVisibilityTests
{
    private sealed class Harness
    {
        public TileMap World = new TileMap(8, 8);
        public BuildingSystem Buildings = MakeBuildings();
        public SkillManager Skills = new();
        public Inv Inventory = new();
        public Game Game = new();

        public Harness()
        {
            for (int x = 0; x < 8; x++)
                for (int y = 0; y < 8; y++)
                    World.Tiles[x, y].Elevation = Constants.SeaLevel + 5f; // 16.5
            World.BuildCornerElevations();
            Inventory.AddItem("dirt_brick", 64);
            Inventory.AddItem("stick", 30);
            Inventory.AddItem("stone", 30);
            Game.Camera = new GameCamera(1280, 720);
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

        public Structure? BrickAt(int tx, int ty, int subX, int subY)
            => Buildings.Structures.FirstOrDefault(s => s.IsActive
                && s.TileX == tx && s.TileY == ty
                && s.SubX == subX && s.SubY == subY);

        /// <summary>Worst (structure key − own-tile key) across a 32-step
        /// yaw sweep — negative means the owning tile quad paints over the
        /// structure at that yaw.</summary>
        public float WorstOwnTileDeficit(Func<float> structureKey, int tx, int ty)
        {
            float worst = float.MaxValue;
            for (int i = 0; i < 32; i++)
            {
                float yaw = i * MathF.PI / 16f;
                Game.Camera!.SetOrbit(yaw);
                float c = MathF.Cos(yaw), s = MathF.Sin(yaw);
                float tile = TileRenderer.TileSortKey(World.GetTile(tx, ty)!, tx, ty, c, s);
                worst = MathF.Min(worst, structureKey() - tile);
            }
            return worst;
        }
    }

    // ── 1. Bricks: far-side sub-cells must never key below their tile ────

    [Fact]
    public void BrickSortKey_FarSideSubCells_NeverBelowOwnTile()
    {
        var h = new Harness();
        Assert.True(h.PlaceBrick(4, 4, 1, 1).Success);
        Assert.True(h.PlaceBrick(6, 6, 0, 0).Success);
        var mid = h.BrickAt(4, 4, 1, 1);
        var corner = h.BrickAt(6, 6, 0, 0);

        float worstMid = h.WorstOwnTileDeficit(
            () => h.Game.StructureSortKey(mid!, h.World, h.Buildings), 4, 4);
        Assert.True(worstMid >= -0.01f,
            $"sub(1,1) brick fell {worstMid:F2} px below its own tile at some yaw — it buries while orbiting");

        float worstCorner = h.WorstOwnTileDeficit(
            () => h.Game.StructureSortKey(corner!, h.World, h.Buildings), 6, 6);
        Assert.True(worstCorner >= -0.01f,
            $"sub(0,0) brick fell {worstCorner:F2} px below its own tile at some yaw — it buries while orbiting");
    }

    // ── 2. Prop structures on sloped ground ─────────────────────────────

    [Fact]
    public void CampfireSortKey_SlopedTile_NeverBelowOwnTile()
    {
        var h = new Harness();
        // Slope the stitch: raise the forward neighbours so the tile's
        // bilinear-center key sits above its raw elevation — today's
        // raw-elevation key loses that gap at EVERY yaw.
        h.World.SetElevationOverride(5, 4, Constants.SeaLevel + 8.5f);
        h.World.SetElevationOverride(4, 5, Constants.SeaLevel + 8.5f);
        Assert.True(h.Place("campfire", 4, 4).Success);
        var camp = h.Buildings.Structures.Single(s => s.StructureId == "campfire");

        float worst = h.WorstOwnTileDeficit(
            () => h.Game.StructureSortKey(camp, h.World, h.Buildings), 4, 4);
        Assert.True(worst >= -0.01f,
            $"campfire on a sloped tile fell {worst:F2} px below its own tile — raised-neighbour stitches bury it");
    }

    [Fact]
    public void CampfireSortKey_FlatPad_TiesOwnTile()
    {
        // Pin: on flat ground the campfire sits exactly at the tile's key —
        // the seq tie-break (sprites enter after tiles) keeps it visible.
        var h = new Harness();
        Assert.True(h.Place("campfire", 4, 4).Success);
        var camp = h.Buildings.Structures.Single(s => s.StructureId == "campfire");

        float worst = h.WorstOwnTileDeficit(
            () => h.Game.StructureSortKey(camp, h.World, h.Buildings), 4, 4);
        Assert.True(worst >= -0.01f,
            $"flat-pad campfire fell {worst:F2} px below its own tile");
    }

    // ── 3. Ground fires ──────────────────────────────────────────────────

    [Fact]
    public void FireSortKey_NeverBelowOwnTile()
    {
        var h = new Harness();
        float worst = h.WorstOwnTileDeficit(
            () => h.Game.FireSortKey(4.5f * Constants.TileSize, 4.5f * Constants.TileSize, h.World),
            4, 4);
        Assert.True(worst >= -0.01f,
            $"ground fire fell {worst:F2} px below its own tile — it sorts behind its own ground");
    }

    // ── 4. Honesty pin: genuine occlusion survives the boost ─────────────

    [Fact]
    public void StructureSortKey_RaisedTileInFront_StillOccludes()
    {
        var h = new Harness();
        Assert.True(h.PlaceBrick(2, 4, 2, 2).Success);
        var brick = h.BrickAt(2, 4, 2, 2);
        // A raised tile TWO tiles east — genuinely between the camera and
        // the brick for every yaw on the +X half of the orbit. (One tile
        // away would stitch into the brick tile's own corners — the entity
        // precedent treats that shared-edge peak as underfoot, not a wall.)
        h.World.SetElevationOverride(4, 4, Constants.SeaLevel + 6f);

        float worstGap = float.MaxValue;
        for (int i = 0; i < 32; i++)
        {
            float yaw = i * MathF.PI / 16f;
            // Genuinely in front: the occluder's positional lead along the
            // view axis must dominate the boost (sin ≥ 0.5 → lead ≥ 64 px
            // vs boost ≤ 45.25). Below that the raised tile is nearly
            // LATERAL — its quad only sliver-overlaps the build, and the
            // boost legitimately wins that sliver, exactly as it does for
            // the player and monsters (the entity precedent).
            if (MathF.Sin(yaw) < 0.5f) continue;
            h.Game.Camera!.SetOrbit(yaw);
            float c = MathF.Cos(yaw), s = MathF.Sin(yaw);
            float raised = TileRenderer.TileSortKey(h.World.GetTile(4, 4)!, 4, 4, c, s);
            float key = h.Game.StructureSortKey(brick!, h.World, h.Buildings);
            worstGap = MathF.Min(worstGap, raised - key);
        }
        Assert.True(worstGap > 0f,
            $"a raised tile 2 tiles in front keys only {worstGap:F2} px above the brick — genuine occlusion must survive the boost");
    }
}
