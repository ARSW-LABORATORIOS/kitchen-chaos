namespace KitchenChaos.Server.Models;

public enum IngredientState
{
    Crudo,
    Picado,
    Cocinando,
    Cocinado,
    Listo,
    Quemado
}

/// <summary>
/// Ingrediente que un jugador tiene en mano, en alguna etapa de preparación.
/// AB#20, AB#21
/// </summary>
public class Ingredient
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string Name { get; set; }
    public IngredientState State { get; set; } = IngredientState.Crudo;
    public required string HeldByConnectionId { get; set; }
}
