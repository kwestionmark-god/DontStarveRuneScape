namespace DontStarveRuneScape.Actions;

using System.Collections.Generic;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Inventory;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Skills.Woodcutting;
using DontStarveRuneScape.Skills.Mining;
using DontStarveRuneScape.Skills.Foraging;
using DontStarveRuneScape.World;
using DontStarveRuneScape.Survival;

/// <summary>
/// ActionSystem — Manages all player resource interaction actions.
/// </summary>
public sealed class ActionSystem
{
    // ─── Public State ────────────────────────────────────────────────────────

    /// <summary>The currently running action (None = idle).</summary>
    public ActiveAction Active { get; private set; } = new(ActionType.Woodcutting);

    /// <summary>Stamina pool for woodcutting/mining.</summary>
    public StaminaPool Stamina { get; } = new();

    /// <summary>Queue of notifications to display.</summary>
    public List<ActionNotification> Notifications { get; } = [];

    // ─── Internal State ──────────────────────────────────────────────────────

    private readonly List<ActionNotification> _pendingNotifications = [];
    public SurvivalSystem? Survival { get; set; }
    private object? _seasonSystem;
    public object? WeatherSystem { get; set; }
    public WoodcuttingSkill? WoodcuttingSkill { get; set; }
    public MiningSkill? MiningSkill { get; set; }
    public ForagingSkill? ForagingSkill { get; set; }
    private TileMap? _tileMap;

    /// <summary>Test hook: forces the next rare-roll result
    /// (null = random, "" = forced no-drop) — the RaidRollOverride
    /// convention.</summary>
    public string? ForceRareDrop { get; set; }

    /// <summary>Seeded RNG for the rare roll (tests; null = new per catch).</summary>
    public System.Random? RareDropRandom { get; set; }

    // ─── Constructor ─────────────────────────────────────────────────────────

    public ActionSystem() { }

    // ─── Wiring ──────────────────────────────────────────────────────────────

    /// <summary>Wire the TileMap so depletion can start regrow timers.</summary>
    public void SetTileMap(TileMap tileMap) => _tileMap = tileMap;

    // ─── Start Action ────────────────────────────────────────────────────────

    /// <summary>
    /// Start an action: checks tools, builds params, returns None or error string.
    /// </summary>
    /// <param name="actionType">The ActionType (WOODCUTTING/MINING/COOKING).</param>
    /// <param name="resource">The ResourceNode to interact with (None for cooking).</param>
    /// <param name="skillManager">For stat-based success rate & bonus values.</param>
    /// <param name="inventory">For equipped-tool detection.</param>
    /// <param name="recipeId">Cooking recipe id (only used for COOKING).</param>
    /// <param name="tileXy">Tile coords for regrow tracking.</param>
    /// <returns>Error message string if action cannot start, None on success.</returns>
    public string? StartAction(
        ActionType actionType,
        ResourceNode? resource,
        SkillManager skillManager,
        Inv inventory,
        string? recipeId = null,
        (int X, int Y)? tileXy = null)
    {
        // Already-busy guard (a second start in the same frame)
        if (Active.State == ActionState.Running)
            return "Already performing an action.";

        // Required-tool check (for gathering) — the matched id is kept:
        // the Fishing branch reads it to pick the rod tier's cast length.
        string? matchedTool = null;
        if (resource != null && resource.ResourceDef != null && !string.IsNullOrEmpty(resource.ResourceDef.ToolRequirement))
        {
            matchedTool = FindEquippedTool(inventory, resource.ResourceDef.ToolRequirement);
            if (matchedTool == null)
                return $"You need a {resource.ResourceDef.ToolRequirement}.";
        }

        // Skill-level gate: high-tier nodes require a minimum level
        if (resource != null && resource.ResourceDef != null)
        {
            int requiredLevel = Math.Max(resource.ResourceDef.Tier, resource.ResourceDef.RequiredLevel);
            if (requiredLevel > 1 && skillManager != null)
            {
                string skillId = actionType.GetSkillId(resource);
                int level = skillManager.GetSkillLevel(skillId);
                if (level < requiredLevel)
                {
                    return $"You need {System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(skillId)} level {requiredLevel} to harvest {resource.ResourceDef.Name}.";
                }
            }
        }

        // Build the action with correct parameters
        var action = new ActiveAction(actionType) { TileXy = tileXy };

        if (actionType == ActionType.Woodcutting && resource != null)
        {
            action.Resource = resource;
            action.XpReward = resource.XpReward;
            action.YieldItem = resource.YieldItem;
            action.YieldQuantity = resource.YieldQuantity;
            action.RequiredTool = resource.ResourceDef?.ToolRequirement;

            if (WoodcuttingSkill != null)
            {
                action.StaminaCost = WoodcuttingSkill.GetEffectiveStaminaCost(3.0f);
                action.SuccessRateBonus = WoodcuttingSkill.GetSuccessRateBonus();
                action.ExtraResourcesBonus = skillManager.GetEffectiveStat("woodcutting", "harvest_boost");
            }
            else
            {
                action.StaminaCost = 3.0f;
                action.SuccessRateBonus = skillManager.GetEffectiveStat("woodcutting", "success_rate") * 1.0f;
                action.ExtraResourcesBonus = skillManager.GetEffectiveStat("woodcutting", "harvest_boost");
            }
        }
        else if (actionType == ActionType.Mining && resource != null)
        {
            action.Resource = resource;
            action.XpReward = resource.XpReward;
            action.YieldItem = resource.YieldItem;
            action.YieldQuantity = resource.YieldQuantity;
            action.RequiredTool = resource.ResourceDef?.ToolRequirement;

            if (MiningSkill != null)
            {
                action.StaminaCost = MiningSkill.GetEffectiveStaminaCost(3.0f);
                action.SuccessRateBonus = MiningSkill.GetSuccessRateBonus();
                action.ExtraResourcesBonus = skillManager.GetEffectiveStat("mining", "extra_resources");
            }
            else
            {
                action.StaminaCost = 3.0f;
                action.SuccessRateBonus = skillManager.GetEffectiveStat("mining", "success_rate") * 1.0f;
                action.ExtraResourcesBonus = skillManager.GetEffectiveStat("mining", "extra_resources");
            }
        }
        else if (actionType == ActionType.Foraging && resource != null)
        {
            action.Resource = resource;
            action.XpReward = resource.XpReward;
            action.YieldItem = resource.YieldItem;
            action.YieldQuantity = resource.YieldQuantity;
            action.RequiredTool = resource.ResourceDef?.ToolRequirement; // Should be None/empty for foraging

            if (ForagingSkill != null)
            {
                action.StaminaCost = 2.0f * (1.0f - ForagingSkill.GetStaminaReduction());
                action.SuccessRateBonus = ForagingSkill.GetSuccessRate() * 100.0f; // Convert to percentage
                action.ExtraResourcesBonus = skillManager.GetSubStatPoints("foraging", "harvest_boost");
            }
            else
            {
                action.StaminaCost = 2.0f;
                action.SuccessRateBonus = skillManager.GetEffectiveStat("foraging", "success_rate") * 1.0f;
                action.ExtraResourcesBonus = skillManager.GetEffectiveStat("foraging", "harvest_boost");
            }
        }
        else if (actionType == ActionType.Fishing && resource != null)
        {
            action.Resource = resource;
            action.XpReward = resource.XpReward;
            action.YieldItem = resource.YieldItem;
            action.YieldQuantity = resource.YieldQuantity;
            action.RequiredTool = resource.ResourceDef?.ToolRequirement;

            // No dedicated fishing-skill class: the registered skill's
            // sub-stats drive success and yield, same shape as the
            // foraging fallback below.
            action.StaminaCost = 2.0f;
            // Family scale: one success_rate point buys the same
            // threshold swing as woodcutting/mining/foraging (×100),
            // not the ×1 the fishing slice shipped.
            action.SuccessRateBonus = skillManager.GetSubStatPoints("fishing", "success_rate") * 100.0f;
            action.ExtraResourcesBonus = skillManager.GetSubStatPoints("fishing", "harvest_boost");

            // Rare-table inputs, frozen at cast start: the fisher's level
            // and invested rare-luck points (the roll describes the cast).
            action.FishingLevel = skillManager.GetSkillLevel("fishing");
            action.FishingLuck = skillManager.GetSubStatPoints("fishing", "rare_luck");

            // Fishing is the one timed gather: the cast holds for the
            // rod tier's window before the catch resolves (the window the
            // rod/line/bobber animation plays in). Tier buys speed: the
            // bone rod casts in 2s where the carved rod takes 3s.
            bool boneRod = matchedTool == "bone_fishing_rod";
            action.DurationRemaining = boneRod
                ? Constants.BoneFishingCastSeconds
                : Constants.FishingCastSeconds;
            AddNotification(boneRod
                ? "You cast your bone rod..."
                : "You cast your line...", (120, 190, 255));

            // Baited cast: one bait per cast, consumed the moment the line
            // hits the water — honest grind: no refund on walk-away cancel,
            // the bait sank with the cast.
            action.Baited = inventory.RemoveItem("fishing_bait", 1);
            if (action.Baited)
                AddNotification("Baited hook...", (150, 220, 160));
        }
        else if (actionType == ActionType.Cooking && recipeId != null)
        {
            action.RecipeId = recipeId;
            action.XpReward = 0.0f;
            action.YieldItem = string.Empty;
            action.StaminaCost = 0.0f;
        }
        else
        {
            return "No valid target.";
        }

        Active = action;
        Active.State = ActionState.Running;
        return null;
    }

    // ─── Process Completion ──────────────────────────────────────────────────

    /// <summary>
    /// Process action completion result: add items, grant XP, show notifications.
    /// </summary>
    /// <param name="result">Structured ActionResult from action update.</param>
    /// <param name="inventory">Player inventory to add items to.</param>
    /// <param name="skillManager">Skill manager for XP granting.</param>
    /// <param name="foodRegistry">Optional food registry for spoilage info.</param>
    public void ProcessCompletion(
        ActionResult? result,
        Inv inventory,
        SkillManager skillManager,
        FoodRegistry? foodRegistry = null)
    {
        if (result == null) return;

        if (!result.Success)
        {
            if (!string.IsNullOrEmpty(result.Message))
                AddNotification(result.Message);
            return;
        }

        // Successful result with yield
        if (!string.IsNullOrEmpty(result.ItemId) && result.Quantity > 0)
        {
            // Determine spoilage for perishable food
            float? spoilageSeconds = null;
            if (foodRegistry != null)
            {
                var food = foodRegistry.Get(result.ItemId);
                if (food != null && food.SpoilageRate > 0)
                    spoilageSeconds = food.SpoilageRate;
            }

            // Capacity pre-check
            if (!inventory.CanAdd(result.ItemId, result.Quantity))
            {
                AddNotification("Inventory is full — nothing was gathered.", (255, 100, 100));
                // A rare that can't fit is lost — but VISIBLY, never a
                // silent vanish (manageable loss, per design).
                if (!string.IsNullOrEmpty(result.RareItemId))
                    AddNotification($"No room — the {result.RareItemId} slipped back into the water!", (255, 100, 100));
            }
            else
            {
                inventory.AddItem(result.ItemId, result.Quantity, spoilageSeconds);
                AddNotification($"+{result.Quantity} {result.ItemId}", (100, 255, 100));

                // Determine skill from active resource
                string skillId = ActionTypeToSkillId();

                // Delegate XP + level-up notification to SkillManager
                var levelUpMessages = skillManager.AddXpWithNotification(skillId, result.Xp);
                foreach (var msg in levelUpMessages)
                    AddNotification(msg, (255, 215, 0));

                // Rare drop grant: the dredged item rides the same
                // completion — gold rare-find notification on success,
                // visible red loss if it can't fit.
                if (!string.IsNullOrEmpty(result.RareItemId))
                {
                    if (inventory.AddItem(result.RareItemId, 1))
                        AddNotification($"+1 {result.RareItemId} — a rare find!", (255, 215, 0));
                    else
                        AddNotification($"No room — the {result.RareItemId} slipped back into the water!", (255, 100, 100));
                }
            }
        }

        // Show success message
        if (!string.IsNullOrEmpty(result.Message))
            AddNotification(result.Message);
    }

    /// <summary>Map the active action's resource/tool to a skill ID string.</summary>
    private string ActionTypeToSkillId()
    {
        return Active.ActionType.GetSkillId(Active.Resource);
    }

    // ─── Update ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Cancel the running action (walk-away, interruption): idle it with
    /// no yield and no completion. Returns true when a running action was
    /// cancelled; false when nothing was running (no-op).
    /// </summary>
    public bool CancelActive()
    {
        if (Active.State != ActionState.Running)
            return false;
        Active.State = ActionState.Idle;
        Active.Resource = null;
        Active.RecipeId = null;
        Active.DurationRemaining = 0f;
        return true;
    }

    /// <summary>
    /// Update the active action. Called every frame.
    /// </summary>
    /// <param name="dt">Delta time in seconds.</param>
    /// <returns>Structured result, or None if no action completed this frame.</returns>
    public ActionResult? Update(float dt)
    {
        Stamina.Tick(dt);

        if (Active.State != ActionState.Running)
            return null;

        // Timed actions (the fishing cast) hold for their window before
        // resolving; every other action has no duration and resolves on
        // this first tick, exactly as before.
        if (Active.DurationRemaining > 0f)
        {
            Active.DurationRemaining -= dt;
            if (Active.DurationRemaining > 0f)
                return null;
            Active.DurationRemaining = 0f;
        }
        return CompleteAction();
    }

    /// <summary>Process the result of a completed action.</summary>
    private ActionResult? CompleteAction()
    {
        var action = Active;

        var result = action.ActionType is ActionType.Woodcutting or ActionType.Mining or ActionType.Foraging or ActionType.Fishing
            ? CompleteGathering(action)
            : null;

        // Actions resolve instantly; return to idle whatever the type (a
        // cooking action previously never reset and would have permanently
        // blocked new actions).
        action.State = ActionState.Idle;
        action.Resource = null;
        action.RecipeId = null;
        return result;
    }

    /// <summary>Process completion of a woodcutting, mining, or foraging action.</summary>
    private ActionResult CompleteGathering(ActiveAction action)
    {
        var resource = action.Resource;
        if (resource == null)
            return ActionResult.Failure("No resource to harvest.");

        // Check seasonal availability (placeholder - would call SeasonSystem)
        if (_seasonSystem != null)
        {
            // SeasonSystem.IsResourceAvailable(resource.ResourceId) would be called here
            // For now, always available
        }

        // Check depletion
        if (resource.IsDepleted)
        {
            return ActionResult.Failure("The resource is depleted.");
        }

        // Check stamina — the pool gates the action: acting is possible while
        // it covers the cost; draining it triggers the forced rest.
        if (!Stamina.Consume(action.StaminaCost))
        {
            return ActionResult.Failure("You are too exhausted. Rest for a moment.");
        }

        // Roll success: base_chance + success_rate_stat
        // Base chance is 50%, bonus adds to threshold (positive = easier)
        var random = new System.Random();
        float successThreshold = 50.0f + action.SuccessRateBonus;
        if (random.NextDouble() * 100.0 < successThreshold)
        {
            // Success
            var harvestResult = resource.Harvest();
            if (string.IsNullOrEmpty(harvestResult.ItemId))
                return ActionResult.Failure("The resource is depleted.");

            // Notify the tile map so the regrow timer starts
            if (resource.IsDepleted && _tileMap != null && action.TileXy.HasValue)
            {
                _tileMap.MarkRegrowing(action.TileXy.Value.X, action.TileXy.Value.Y);
            }

            // Determine yield using skill classes
            int quantity = action.YieldQuantity;
            if (action.ActionType == ActionType.Woodcutting && WoodcuttingSkill != null)
            {
                quantity = WoodcuttingSkill.CalculateYield(action.YieldQuantity, action.ExtraResourcesBonus);
            }
            else if (action.ActionType == ActionType.Mining && MiningSkill != null)
            {
                quantity = MiningSkill.CalculateYield(action.YieldQuantity, action.ExtraResourcesBonus);
            }
            else if (action.ActionType == ActionType.Foraging && ForagingSkill != null)
            {
                quantity = ForagingSkill.CalculateHarvest(action.YieldQuantity, action.ExtraResourcesBonus);
            }
            else if (action.ActionType == ActionType.Fishing)
            {
                // Fishing yield arm (the harvest_boost consumer — this
                // stat was stashed but dead until this slice): same +1
                // chance shape as the sibling gatherers, raw points.
                if (action.ExtraResourcesBonus > 0f
                    && new System.Random().NextDouble() * 100.0 < action.ExtraResourcesBonus)
                    quantity++;
            }

            // Apply seasonal resource multiplier to yield (placeholder)
            if (_seasonSystem != null && resource != null)
            {
                // float seasonMod = SeasonSystem.GetResourceMultiplier(resource.Category);
                // quantity = Math.Max(1, (int)(quantity * seasonMod));
            }

            // Apply weather outdoor crafting modifier (placeholder)
            if (WeatherSystem != null)
            {
                // var effects = WeatherSystem.GetEffects();
                // float outdoorMod = effects.GetValueOrDefault("outdoor_crafting", 1.0f);
                // quantity = Math.Max(1, (int)(quantity * outdoorMod));
            }

            // Baited fishing casts land a doubled catch — the bait economy:
            // one consumed bait buys two fish where an unbaited cast lands
            // one.
            bool baited = action.ActionType == ActionType.Fishing && action.Baited;
            if (baited)
                quantity *= 2;

            // Rare drop: one roll per successful catch, scaled by the
            // level stashed at cast start. Yield stays the bait economy's
            // knob; this is the level economy's knob — the keep-casting
            // hook (pearl first, then the old-boot gag).
            string? rareItemId = null;
            if (action.ActionType == ActionType.Fishing)
            {
                rareItemId = ForceRareDrop != null
                    ? (ForceRareDrop.Length == 0 ? null : ForceRareDrop)
                    : FishingRareTable.Roll(
                        (float)(RareDropRandom ?? new System.Random()).NextDouble(),
                        action.FishingLevel,
                        action.FishingLuck);
            }

            string message = baited
                ? $"Harvested {quantity} {action.YieldItem} (bait)."
                : $"Harvested {quantity} {action.YieldItem}.";
            if (rareItemId != null)
                message += $" You dredged up a {rareItemId}!";

            // Return structured result
            return ActionResult.SuccessResult(
                action.YieldItem,
                quantity,
                action.XpReward,
                message,
                rareItemId);
        }
        else
        {
            // Failure — no yield; the stamina already spent gates the next try
            return ActionResult.Failure("You fail to harvest the resource.");
        }
    }

    /// <summary>
    /// Find a tool of the given type in the inventory.
    /// </summary>
    private string? FindEquippedTool(Inv inventory, string toolType)
    {
        // Exact id match or a suffix match ("stone_axe" for "axe") — NOT a
        // substring match: "axe" in "pickaxe" is True, which made a pickaxe
        // satisfy an axe requirement.
        static bool Matches(string itemId, string toolType)
        {
            return itemId == toolType || itemId.EndsWith("_" + toolType);
        }

        // Check equipped slots first
        foreach (var slot in inventory.Slots)
        {
            if (slot != null && slot.IsEquipped && slot.ItemId != null)
            {
                if (Matches(slot.ItemId, toolType))
                    return slot.ItemId;
            }
        }

        // Also check non-equipped
        foreach (var slot in inventory.Slots)
        {
            if (slot != null && slot.ItemId != null)
            {
                if (Matches(slot.ItemId, toolType))
                    return slot.ItemId;
            }
        }

        return null;
    }

    // ─── Season/Weather System Properties ────────────────────────────────────

    public object? SeasonSystem
    {
        get => _seasonSystem;
        set => _seasonSystem = value;
    }

    // ─── Notifications ───────────────────────────────────────────────────────

    /// <summary>Queue a notification to display.</summary>
    public void AddNotification(string text, (byte R, byte G, byte B) color = default)
    {
        _pendingNotifications.Add(new ActionNotification(text, color));
    }

    /// <summary>Flush pending notifications into the active display queue.</summary>
    public List<ActionNotification> FlushNotifications()
    {
        var result = new List<ActionNotification>(_pendingNotifications);
        Notifications.AddRange(result);
        _pendingNotifications.Clear();
        return result;
    }

    /// <summary>
    /// Tick active notifications. Expire old ones, collect new ones.
    /// </summary>
    /// <param name="dt">Delta time in seconds.</param>
    /// <returns>List of just-expired notifications (for cleanup).</returns>
    public List<ActionNotification> UpdateNotifications(float dt)
    {
        // Flush new ones first
        FlushNotifications();

        // Expire old notifications
        var expired = new List<ActionNotification>();
        for (int i = Notifications.Count - 1; i >= 0; i--)
        {
            var notif = Notifications[i];
            notif.Elapsed += dt;
            if (notif.IsExpired)
            {
                expired.Add(notif);
                Notifications.RemoveAt(i);
            }
        }
        return expired;
    }
}
