namespace KitchenChaos.Server.Models;

/// <summary>
/// Define que preparacion necesita un ingrediente: picarse, cocinarse, ambas o ninguna
/// (los que se agregan directo, como la leche condensada). AB#78.
/// </summary>
public class IngredientDefinition
{
    public required string Name { get; set; }
    public bool RequiresChop { get; set; }
    public bool RequiresCook { get; set; }
}
