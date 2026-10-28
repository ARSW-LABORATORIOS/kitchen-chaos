namespace KitchenChaos.Server.Models;

/// <summary>
/// Punto fijo de la cocina donde hay un ingrediente disponible para recoger.
/// AB#19
/// </summary>
public class Station
{
    public required string Id { get; set; }
    public required string IngredientName { get; set; }
    public bool IsAvailable { get; set; } = true;
}
