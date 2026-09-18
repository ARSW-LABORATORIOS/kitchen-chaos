namespace KitchenChaos.Server.Models;

/// <summary>
/// Representa una receta válida del nivel: un plato con sus ingredientes requeridos.
/// AB#27
/// </summary>
public class Recipe
{
    /// <summary>Nombre del plato (ej. "Ensalada", "Hamburguesa").</summary>
    public string DishName { get; set; } = string.Empty;

    /// <summary>Lista de ingredientes necesarios para completar el plato.</summary>
    public List<string> Ingredients { get; set; } = new();
}
