namespace DontStarveRuneScape.UI;

/// <summary>
/// FireMenuPanel — the fire venue: recipes that need a fire (cooking and
/// other heat work) plus rest-until-dawn, opened by E near a lit fire or
/// heat station. Recipe content is single-sourced by IsFireRecipe.
/// </summary>
public sealed class FireMenuPanel
{
    /// <summary>Single source of the fire-menu partition: a recipe belongs
    /// here when it requires a campfire/heat station or trains cooking.</summary>
    public static bool IsFireRecipe(Data.CraftRecipe recipe)
        => recipe.RequiresCampfire || recipe.RequiredSkill == "cooking";
}
