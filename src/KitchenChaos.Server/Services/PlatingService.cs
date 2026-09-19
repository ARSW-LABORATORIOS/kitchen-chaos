using System.Collections.Concurrent;
using KitchenChaos.Server.Models;

namespace KitchenChaos.Server.Services;

public class PlateResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Plate? Plate { get; set; }
}

public class PlatingService
{
    private readonly PreparationService _preparationService;
    private readonly OrderService _orderService;

    private readonly ConcurrentDictionary<string, List<Plate>> _platesByRoom = new();

    public PlatingService(
        PreparationService preparationService,
        OrderService orderService)
    {
        _preparationService = preparationService;
        _orderService = orderService;
    }

    public PlateResult PlateDish(
        string roomCode,
        string connectionId,
        List<string> ingredientIds)
    {
        var order = _orderService.GetActiveOrder(roomCode);

        if (order is null)
        {
            return new PlateResult
            {
                Success = false,
                Error = "No hay un pedido activo."
            };
        }

        var ingredients = _preparationService.GetPlayerIngredients(
            roomCode,
            connectionId,
            ingredientIds);

        if (ingredients.Count != ingredientIds.Distinct().Count())
        {
            return new PlateResult
            {
                Success = false,
                Error = "Uno o más ingredientes no pertenecen al jugador."
            };
        }

        if (ingredients.Any(i => i.State == IngredientState.Crudo ||
                                 i.State == IngredientState.Cocinando ||
                                 i.State == IngredientState.Quemado))
        {
            return new PlateResult
            {
                Success = false,
                Error = "Hay ingredientes que todavía no están listos para emplatar."
            };
        }

        var selectedNames = ingredients
            .Select(i => i.Name)
            .OrderBy(x => x)
            .ToList();

        var requiredNames = order.RequiredIngredients
            .OrderBy(x => x)
            .ToList();

        if (!selectedNames.SequenceEqual(requiredNames))
        {
            return new PlateResult
            {
                Success = false,
                Error = "Los ingredientes no coinciden con el pedido actual."
            };
        }

        var removed = _preparationService.RemovePlayerIngredients(
            roomCode,
            connectionId,
            ingredientIds);

        if (!removed)
        {
            return new PlateResult
            {
                Success = false,
                Error = "No fue posible consumir los ingredientes seleccionados."
            };
        }

        var plates = _platesByRoom.GetOrAdd(
            roomCode,
            _ => new List<Plate>());

        Plate plate;

        lock (plates)
        {
            plate = plates.FirstOrDefault(p => p.State == PlateState.Available)
                ?? new Plate();

            if (!plates.Contains(plate))
                plates.Add(plate);

            plate.State = PlateState.Ready;
            plate.DishName = order.DishName;
            plate.Ingredients = selectedNames;
        }

        return new PlateResult
        {
            Success = true,
            Plate = plate
        };
    }

    public PlateResult TakePlateForDelivery(string roomCode, string plateId)
    {
        if (!_platesByRoom.TryGetValue(roomCode, out var plates))
        {
            return new PlateResult
            {
                Success = false,
                Error = "No hay platos en esta sala."
            };
        }

        lock (plates)
        {
            var plate = plates.FirstOrDefault(p => p.Id == plateId);

            if (plate is null)
            {
                return new PlateResult
                {
                    Success = false,
                    Error = "El plato no existe."
                };
            }

            if (plate.State != PlateState.Ready)
            {
                return new PlateResult
                {
                    Success = false,
                    Error = "El plato no está listo para entregar."
                };
            }

            plate.State = PlateState.Dirty;

            return new PlateResult
            {
                Success = true,
                Plate = plate
            };
        }
    }

    public PlateResult WashPlate(string roomCode, string plateId)
    {
        if (!_platesByRoom.TryGetValue(roomCode, out var plates))
        {
            return new PlateResult
            {
                Success = false,
                Error = "No hay platos en esta sala."
            };
        }

        lock (plates)
        {
            var plate = plates.FirstOrDefault(p => p.Id == plateId);

            if (plate is null)
            {
                return new PlateResult
                {
                    Success = false,
                    Error = "El plato no existe."
                };
            }

            if (plate.State != PlateState.Dirty)
            {
                return new PlateResult
                {
                    Success = false,
                    Error = "El plato no está sucio."
                };
            }

            plate.State = PlateState.Available;
            plate.DishName = null;
            plate.Ingredients.Clear();

            return new PlateResult
            {
                Success = true,
                Plate = plate
            };
        }
    }

    public Plate? GetPlate(string roomCode, string plateId)
    {
        if (!_platesByRoom.TryGetValue(roomCode, out var plates))
            return null;

        lock (plates)
        {
            return plates.FirstOrDefault(p => p.Id == plateId);
        }
    }
}