namespace KitchenChaos.Server.Models;

/// <summary>
/// Representa un pedido activo generado para una sala de juego.
/// AB#27, AB#28
/// </summary>
public class Order
{
    /// <summary>Identificador único del pedido.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Nombre del plato que se debe preparar.</summary>
    public string DishName { get; set; } = string.Empty;

    /// <summary>Ingredientes requeridos para completar el pedido.</summary>
    public List<string> RequiredIngredients { get; set; } = new();
}
