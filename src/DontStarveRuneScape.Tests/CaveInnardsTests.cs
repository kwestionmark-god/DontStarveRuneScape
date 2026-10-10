namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Combat;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.World;
using Xunit;

/// <summary>
/// Cave innards regression pins (the 566ed8b regression): the cave must
/// place the REAL registry ore defs (sprite_keys render — no green
/// fallback squares), deterministically per seed, and its combat system
/// must contain the cave troll (the direct spawn — SpawnFromRegistry is
/// biome-keyed and the cave biome "cavern" has no monsters.json entries).
/// Spec: docs/superpowers/specs/2026-10-09-cave-innards-regression-fix.md
/// </summary>
public class CaveInnardsTests
{
    private static ResourceRegistry RealResources()
    {
        // Bootstrap converts raw rows inline (Bootstrap.cs:53-83) — mirror
        // the fields the vein defs actually carry (sprite_key above all).
        var loader = new DataLoader();
        loader.LoadAll();
        return new ResourceRegistry(loader.ResourcesData.Select(d =>
        {
            var def = new ResourceDef();
            foreach (var kv in d)
            {
                var v = kv.Value?.ToString() ?? string.Empty;
                switch (kv.Key.ToLowerInvariant())
                {
                    case "id": def.Id = v; break;
                    case "name": def.Name = v; break;
                    case "biome": def.Biome = v; break;
                    case "category": def.Category = v; break;
                    case "yield_item": def.YieldItem = v; break;
                    case "yield_quantity": def.Yield = int.TryParse(v, out var y) ? y : 1; break;
                    case "xp_reward": def.Xp = float.TryParse(v, out var xp) ? xp : 1f; break;
                    case "depletion_count": def.DepletionCount = int.TryParse(v, out var dc) ? dc : 1; break;
                    case "regrow_time": def.Regrow = float.TryParse(v, out var rt) ? rt : 0f; break;
                    case "sprite_key": def.SpriteKey = v; break;
                    case "required_level": def.RequiredLevel = int.TryParse(v, out var rl) ? rl : 1; break;
                }
            }
            return def;
        }));
    }

    [Fact]
    public void CaveOreNodes_UseRealRegistryDefs_WithSpriteKeys()
    {
        // The 566ed8b regression built inline ResourceDefs with NO
        // SpriteKey — the renderer's texture miss falls back to green
        // crossed planes (the "green squares" report). Registry defs
        // carry sprite keys, so ore renders as ore.
        var cave = CaveWorldSystem.Generate(12345, RealResources(), null);

        var oreNodes = AllTiles(cave).Where(t => t.ResourceNode != null).ToList();
        Assert.NotEmpty(oreNodes);
        Assert.All(oreNodes, tile =>
        {
            var def = tile.ResourceNode!.ResourceDef;
            Assert.NotNull(def);
            Assert.False(string.IsNullOrEmpty(def!.SpriteKey),
                $"ore node at ({tile.X},{tile.Y}) has no sprite_key — green fallback square");
        });
    }

    [Fact]
    public void CaveOreNodes_AreDeterministicPerSeed()
    {
        // The regression also swapped the seeded vein layout for an
        // unseeded Random() — the same cave must regenerate identically
        // (cave-expedition persistence depends on it).
        var a = CaveWorldSystem.Generate(777, RealResources(), null);
        var b = CaveWorldSystem.Generate(777, RealResources(), null);

        var oreA = AllTiles(a).Where(t => t.ResourceNode != null)
            .Select(t => (t.X, t.Y, t.ResourceNode!.ResourceId)).ToList();
        var oreB = AllTiles(b).Where(t => t.ResourceNode != null)
            .Select(t => (t.X, t.Y, t.ResourceNode!.ResourceId)).ToList();

        Assert.Equal(oreA, oreB);
    }

    [Fact]
    public void CaveCombat_ContainsTheCaveTroll()
    {
        // The regression swapped the direct troll spawn for
        // SpawnFromRegistry — biome-keyed on "cavern", which has NO
        // monsters.json entries, so caves spawned nothing at all.
        // The direct cross-biome lookup is the contract.
        var loader = new DataLoader();
        loader.LoadAll();
        var monsters = new MonsterRegistry();
        monsters.LoadAll();
        var cave = CaveWorldSystem.Generate(12345, null, null);

        var combat = CaveWorldSystem.CreateCaveCombat(monsters, null, cave);

        Assert.Contains(combat.Monsters, m => m.MonsterId == "cave_troll");
    }

    private static IEnumerable<Tile> AllTiles(TileMap map)
    {
        for (int x = 0; x < map.Width; x++)
            for (int y = 0; y < map.Height; y++)
                yield return map.Tiles[x, y];
    }
}
