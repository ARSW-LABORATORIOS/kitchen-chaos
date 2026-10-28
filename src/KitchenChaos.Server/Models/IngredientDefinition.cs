namespace KitchenChaos.Server.Models;

/// <summary>
/// Define que preparacion necesita un ingrediente: picarse, y si se cocina, hervido o frito
/// (los que no necesitan nada, como la leche condensada, quedan con CookMethod.Ninguno). AB#78, AB#82.
/// </summary>
public class IngredientDefinition
{
    public required string Name { get; set; }
    public bool RequiresChop { get; set; }
    public CookMethod CookMethod { get; set; } = CookMethod.Ninguno;
    public bool RequiresCook => CookMethod != CookMethod.Ninguno;
}
