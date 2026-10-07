namespace DontStarveRuneScape.Tests;

using System;
using System.IO;
using System.Linq;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Input;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.UI;
using Silk.NET.Input;
using Xunit;

/// <summary>
/// Character backgrounds (frontier slice 3): character creation offers
/// Wanderer/Forester/Prospector/Scavenger; the choice maps to the already
/// existing starter packs, flows StartNewGame → PendingCharacterDef →
/// Bootstrap, and persists in the save.
/// Spec: docs/superpowers/specs/2026-10-07-character-backgrounds-design.md
/// </summary>
public class CharacterBackgroundTests
{
    // -- 1. The catalog maps backgrounds to the existing packs ----------

    [Theory]
    [InlineData("wanderer", "default")]
    [InlineData("forester", "forester")]
    [InlineData("prospector", "prospector")]
    [InlineData("scavenger", "scavenger")]
    public void Backgrounds_ById_MapToStarterPacks(string id, string expectedPack)
    {
        var bg = Backgrounds.ById(id);
        Assert.Equal(expectedPack, bg.PackId);
        Assert.False(string.IsNullOrWhiteSpace(bg.Name));
        Assert.False(string.IsNullOrWhiteSpace(bg.Hint));
    }

    [Fact]
    public void Backgrounds_UnknownId_FallsBackToWanderer()
    {
        Assert.Equal("wanderer", Backgrounds.ById("nope").Id);
        Assert.Equal("default", Backgrounds.ById("nope").PackId);
    }

    [Fact]
    public void StarterPacks_BackgroundItems_MatchDesign()
    {
        // The catalog's pack ids must resolve to real packs with the
        // designed tools (the packs themselves predate this slice).
        Assert.Equal("axe", StarterPack.GetStarterPack("forester").Tool);
        Assert.Equal("pickaxe", StarterPack.GetStarterPack("prospector").Tool);
        Assert.Equal("torch", StarterPack.GetStarterPack("scavenger").Tool);
        Assert.Equal("torch", StarterPack.GetStarterPack("default").Tool);
    }

    // -- 2. The panel selects a background ------------------------------

    [Fact]
    public void Panel_SelectsBackground_ByClick()
    {
        var game = new Game();
        var captured = default(UI.CharacterDefinition?);
        game.CharacterSelectPanel = new CharacterSelectPanel();
        game.CharacterSelectPanel.SetConfirmCallback(def => captured = def);

        var panel = game.CharacterSelectPanel;
        // Select each option in turn by clicking its region, then read the
        // selection back through the panel's public surface.
        foreach (var (id, region) in CharacterBackgroundRegions())
        {
            var click = new InputState
            {
                MouseX = region.cx, MouseY = region.cy, MouseLeftClick = true,
            };
            panel.Update(game, click, 1280, 720);
            Assert.Equal(id, panel.SelectedBackgroundId);
        }
    }

    [Fact]
    public void Panel_Confirm_PassesNameAndBackground()
    {
        var game = new Game();
        game.CharacterSelectPanel = new CharacterSelectPanel();
        UI.CharacterDefinition? captured = null;
        game.CharacterSelectPanel.SetConfirmCallback(def => captured = def);
        var panel = game.CharacterSelectPanel;

        panel.HandleTextInput('R');
        panel.HandleTextInput('i');
        // Click the forester option, then BEGIN (center 570,415: the button
        // spans x 508..632 at top+158 = 394..436).
        var (cx, cy) = CharacterBackgroundRegions().Single(r => r.id == "forester").region;
        panel.Update(game, new InputState { MouseX = cx, MouseY = cy, MouseLeftClick = true }, 1280, 720);
        // BEGIN moved below the background row: top+210 → y 446..488.
        panel.Update(game, new InputState { MouseX = 570f, MouseY = 467f, MouseLeftClick = true }, 1280, 720);

        Assert.NotNull(captured);
        Assert.Equal("Ri", captured!.Name);
        Assert.Equal("forester", captured.Background);
    }

    [Fact]
    public void Panel_Keyboard_CyclesAndConfirms()
    {
        var game = new Game();
        game.CharacterSelectPanel = new CharacterSelectPanel();
        UI.CharacterDefinition? captured = null;
        game.CharacterSelectPanel.SetConfirmCallback(def => captured = def);
        var panel = game.CharacterSelectPanel;

        Assert.Equal("wanderer", panel.SelectedBackgroundId);
        panel.HandleKey(game, Key.Right);
        Assert.Equal("forester", panel.SelectedBackgroundId);
        panel.HandleKey(game, Key.Right);
        Assert.Equal("prospector", panel.SelectedBackgroundId);
        panel.HandleKey(game, Key.Left);
        Assert.Equal("forester", panel.SelectedBackgroundId);
        panel.HandleKey(game, Key.Enter);

        Assert.NotNull(captured);
        Assert.Equal("forester", captured!.Background);
        Assert.Equal("Survivor", captured.Name); // no typed name → default
    }

    // -- 3. StartNewGame wires the background ----------------------------

    [Fact]
    public void StartNewGame_AppliesBackgroundPack()
    {
        var game = new Game();
        game.StartNewGame("Riri", "forester");

        Assert.NotNull(game.PendingCharacterDef);
        Assert.Equal("forester", game.PendingCharacterDef!.Background);
        Assert.Equal("forester", game.PendingCharacterDef.StarterPackId);
        Assert.Equal("Riri", game.PendingCharacterDef.Name);
    }

    [Fact]
    public void StartNewGame_OldOverload_DefaultsToWanderer()
    {
        var game = new Game();
        game.StartNewGame("Solo");

        Assert.Equal("wanderer", game.PendingCharacterDef!.Background);
        Assert.Equal("default", game.PendingCharacterDef.StarterPackId);
    }

    // -- 4. The background survives the save round-trip ------------------

    [Fact]
    public void SaveRoundTrip_KeepsBackground()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dsr-bg-{Guid.NewGuid():N}");
        var save = new SaveSystem(dir);

        var game = new Game();
        game.StartNewGame("Riri", "prospector");
        // The save snapshot reads the name from Player (Bootstrap creates it
        // in a real session); stage it the way a booted game would look.
        game.Player = new DontStarveRuneScape.Core.Player(100f, 100f) { Name = "Riri" };
        save.Save(game, 0);

        var loaded = save.Load(0);
        Assert.NotNull(loaded);
        Assert.Equal("prospector", loaded!.CharacterBackground);
        Assert.Equal("Riri", loaded.CharacterName);
    }

    [Fact]
    public void OldSave_WithoutBackground_LoadsAsWanderer()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dsr-bg-old-{Guid.NewGuid():N}");
        var save = new SaveSystem(dir);
        var game = new Game();
        game.StartNewGame("OldTimer");
        save.Save(game, 0);

        // Strip the field the way a pre-slice save would look (truncate:
        // OpenWrite alone would leave the old tail behind).
        var path = Path.Combine(dir, "slot_0.json");
        var json = File.ReadAllText(path);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name != "CharacterBackground")
                    prop.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        var loaded = save.Load(0);
        Assert.NotNull(loaded);
        Assert.Equal("wanderer", loaded!.CharacterBackground);
    }

    // -- Helpers -----------------------------------------------------------

    /// <summary>Click regions for the four background options in the panel
    /// at 1280x720: name box center-y + background row. Mirrors the layout
    /// the panel renders (row under the name input, four equal cells).</summary>
    private static (string id, (float cx, float cy) region)[] CharacterBackgroundRegions()
    {
        // Panel top = 720*0.5 - 124 = 236; name box at top+101 = 337;
        // background row begins at top+140 (center y top+158), width 480
        // split into 4 cells of 120 starting at cx-240.
        float left = 640f - 240f;
        float rowCy = 236f + 158f;
        return
        [
            ("wanderer", (left + 60f, rowCy)),
            ("forester", (left + 180f, rowCy)),
            ("prospector", (left + 300f, rowCy)),
            ("scavenger", (left + 420f, rowCy)),
        ];
    }
}
