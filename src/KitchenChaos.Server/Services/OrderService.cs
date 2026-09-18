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
    /// Recetas válidas del nivel 1. Cada receta define un plato y sus ingredientes requeridos.
    /// AB#27
    /// </summary>
    private static readonly List<Recipe> Recipes =
    [
        new() { DishName = "Ensalada",      Ingredients = ["lechuga", "tomate"] },
        new() { DishName = "Hamburguesa",   Ingredients = ["pan", "carne", "lechuga"] },
        new() { DishName = "Sopa",          Ingredients = ["zanahoria", "papa", "caldo"] },
        new() { DishName = "Sandwich",      Ingredients = ["pan", "queso", "tomate"] },
    ];

    // Un pedido activo por sala. ConcurrentDictionary porque varias salas
    // pueden generar o consultar pedidos al mismo tiempo desde hilos distintos.
    private readonly ConcurrentDictionary<string, Order> _activeOrders = new();

    /// <summary>
    /// Genera un pedido aleatorio válido para la sala indicada y lo almacena como pedido activo.
    /// Si ya existía un pedido para esa sala, lo reemplaza.
    /// AB#27
    /// </summary>
    /// <param name="roomCode">Código de la sala para la que se genera el pedido.</param>
    /// <returns>El pedido generado.</returns>
    public Order GenerateOrder(string roomCode)
    {
        var recipe = Recipes[Random.Shared.Next(Recipes.Count)];
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
