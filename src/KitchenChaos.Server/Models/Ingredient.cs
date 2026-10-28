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

/// <summary>Como se cocina un ingrediente, si le hace falta. AB#82.</summary>
public enum CookMethod
{
    Ninguno,
    Hervir,
    Freir
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

    // AB#78 - que preparacion necesita este ingrediente en particular.
    public bool RequiresChop { get; set; } = true;

    // AB#82 - antes era un bool RequiresCook; ahora distingue hervir de freír
    // para que las recetas de los niveles nuevos puedan pedir uno u otro.
    public CookMethod CookMethod { get; set; } = CookMethod.Hervir;
    public bool RequiresCook => CookMethod != CookMethod.Ninguno;
}
