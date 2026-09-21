using System.Collections.Concurrent;
using KitchenChaos.Server.Models;

namespace KitchenChaos.Server.Services;

/// <summary>
/// Gestiona los pedidos activos por sala: generación dinámica y validación de entregas.
/// AB#27: genera pedidos válidos según las recetas del nivel.
/// AB#28: valida si el pedido entregado coincide con la receta esperada.
/// </summary>
public class OrderService
{
    /// <summary>
    /// Recetas configurables por nivel. Agregar un nivel nuevo es agregar recetas aqui,
    /// no toca GameHub. AB#78.
    /// Nivel 1: arroz con leche. Nivel 2: ensalada (combinaciones variables).
    /// </summary>
    private static readonly List<Recipe> Recipes =
    [
        new() { Level = 1, DishName = "Arroz con leche",            Ingredients = ["leche", "arroz", "leche condensada"] },
        new() { Level = 1, DishName = "Arroz con leche con pasas",  Ingredients = ["leche", "arroz", "leche condensada", "uvas pasas"] },

        new() { Level = 2, DishName = "Ensalada", Ingredients = ["tomate", "lechuga"] },
        new() { Level = 2, DishName = "Ensalada", Ingredients = ["tomate", "pepino", "cebolla"] },
        new() { Level = 2, DishName = "Ensalada", Ingredients = ["lechuga", "pepino"] },
    ];

    private readonly RoomService _roomService;

    // Un pedido activo por sala. ConcurrentDictionary porque varias salas
    // pueden generar o consultar pedidos al mismo tiempo desde hilos distintos.
    private readonly ConcurrentDictionary<string, Order> _activeOrders = new();

    public OrderService(RoomService roomService)
    {
        _roomService = roomService;
    }

    /// <summary>Ingredientes que usa al menos una receta del nivel indicado. AB#78.</summary>
    public List<string> GetIngredientsForLevel(int level) =>
        Recipes
            .Where(r => r.Level == level)
            .SelectMany(r => r.Ingredients)
            .Distinct()
            .ToList();

    private Recipe PickRecipe(string roomCode)
    {
        var level = _roomService.GetRoom(roomCode)?.Level ?? 1;
        var recipesForLevel = Recipes.Where(r => r.Level == level).ToList();

        return recipesForLevel[Random.Shared.Next(recipesForLevel.Count)];
    }

    /// <summary>
    /// Genera un pedido aleatorio válido para la sala indicada y lo almacena como pedido activo.
    /// Si ya existía un pedido para esa sala, lo reemplaza.
    /// AB#27
    /// </summary>
    /// <param name="roomCode">Código de la sala para la que se genera el pedido.</param>
    /// <returns>El pedido generado.</returns>
    public Order GenerateOrder(string roomCode)
    {
        var recipe = PickRecipe(roomCode);
        var order = new Order
        {
            DishName = recipe.DishName,
            RequiredIngredients = new List<string>(recipe.Ingredients)
        };

        _activeOrders[roomCode] = order;
        return order;
    }

    /// <summary>
    /// Devuelve el pedido activo de una sala. Null si no hay pedido generado aún.
    /// AB#27
    /// </summary>
    /// <param name="roomCode">Código de la sala.</param>

    public Order GetOrCreateActiveOrder(string roomCode)
    {
        return _activeOrders.GetOrAdd(roomCode, _ =>
        {
            var recipe = PickRecipe(roomCode);

            return new Order
            {
                DishName = recipe.DishName,
                RequiredIngredients = new List<string>(recipe.Ingredients)
            };
        });
    }

    public Order? GetActiveOrder(string roomCode) =>
        _activeOrders.TryGetValue(roomCode, out var order) ? order : null;

    /// <summary>
    /// Valida si los ingredientes entregados coinciden exactamente con los del pedido activo.
    /// Si es correcto, elimina el pedido activo (el siguiente se genera al llamar GenerateOrder).
    /// AB#28
    /// </summary>
    /// <param name="roomCode">Código de la sala que entrega el pedido.</param>
    /// <param name="deliveredIngredients">Ingredientes que el jugador entrega.</param>
    /// <returns>True si el pedido es correcto; false si no coincide o no hay pedido activo.</returns>
    public bool ValidateDelivery(string roomCode, List<string> deliveredIngredients)
    {
        if (!_activeOrders.TryGetValue(roomCode, out var order))
            return false;

        var expected = order.RequiredIngredients.OrderBy(i => i).ToList();
        var delivered = deliveredIngredients.OrderBy(i => i).ToList();

        if (!expected.SequenceEqual(delivered))
            return false;

        // Pedido correcto: se elimina para que el siguiente pueda generarse
        _activeOrders.TryRemove(roomCode, out _);
        return true;
    }
}
