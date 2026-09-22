using System.Collections.Concurrent;
using KitchenChaos.Server.Hubs;
using KitchenChaos.Server.Models;
using Microsoft.AspNetCore.SignalR;

namespace KitchenChaos.Server.Services;

public class TakeIngredientResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Ingredient? Ingredient { get; set; }
}

public class IngredientActionResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Maneja las estaciones de una sala y los ingredientes que los jugadores van preparando.
/// AB#19: recoger ingrediente de una estación.
/// AB#20: picar ingrediente.
/// AB#21: cocinar ingrediente (avanza solo con el tiempo, se quema si se deja demasiado).
/// </summary>
public class PreparationService
{
    private const int CookSeconds = 4;
    private const int BurnSeconds = 8;
    private const int RestockSeconds = 2;

    /// <summary>Que preparacion necesita cada ingrediente. AB#78. Si uno no esta aqui, se asume picar+cocinar.</summary>
    private static readonly Dictionary<string, IngredientDefinition> IngredientDefinitions = new()
    {
        ["lechuga"] = new() { Name = "lechuga", RequiresChop = true, RequiresCook = false },
        ["tomate"] = new() { Name = "tomate", RequiresChop = true, RequiresCook = false },
        ["pepino"] = new() { Name = "pepino", RequiresChop = true, RequiresCook = false },
        ["cebolla"] = new() { Name = "cebolla", RequiresChop = true, RequiresCook = false },
        ["leche"] = new() { Name = "leche", RequiresChop = false, RequiresCook = true },
        ["arroz"] = new() { Name = "arroz", RequiresChop = false, RequiresCook = true },
        ["leche condensada"] = new() { Name = "leche condensada", RequiresChop = false, RequiresCook = false },
        ["uvas pasas"] = new() { Name = "uvas pasas", RequiresChop = false, RequiresCook = false },
    };

    // Estaciones por sala, segun el nivel. Se crean la primera vez que se piden.
    private readonly ConcurrentDictionary<string, List<Station>> _stationsByRoom = new();

    // Ingredientes en preparación por sala (en mano de algún jugador).
    private readonly ConcurrentDictionary<string, List<Ingredient>> _ingredientsByRoom = new();

    private readonly IHubContext<GameHub> _hub;
    private readonly RoomService _roomService;
    private readonly OrderService _orderService;

    public PreparationService(IHubContext<GameHub> hub, RoomService roomService, OrderService orderService)
    {
        _hub = hub;
        _roomService = roomService;
        _orderService = orderService;
    }

    private static IngredientDefinition GetDefinition(string ingredientName) =>
        IngredientDefinitions.GetValueOrDefault(
            ingredientName,
            new IngredientDefinition { Name = ingredientName, RequiresChop = true, RequiresCook = true });

    /// <summary>AB#19, AB#78 - Estaciones de la sala segun los ingredientes que pide su nivel actual.</summary>
    public List<Station> GetStations(string roomCode) =>
        _stationsByRoom.GetOrAdd(roomCode, _ =>
        {
            var level = _roomService.GetRoom(roomCode)?.Level ?? 1;

            return _orderService.GetIngredientsForLevel(level)
                .Select(name => new Station
                {
                    Id = Guid.NewGuid().ToString(),
                    IngredientName = name
                }).ToList();
        });

    /// <summary>AB#19 - Recoger un ingrediente. El lock evita que dos jugadores tomen el mismo a la vez.</summary>
    public TakeIngredientResult TakeIngredient(string roomCode, string stationId, string connectionId)
    {
        var station = GetStations(roomCode).FirstOrDefault(s => s.Id == stationId);
        if (station is null)
            return new TakeIngredientResult { Success = false, Error = "La estación no existe." };

        lock (station)
        {
            if (!station.IsAvailable)
                return new TakeIngredientResult { Success = false, Error = "Otro jugador ya tomó este ingrediente." };

            station.IsAvailable = false;
        }

        _ = RestockStationAsync(roomCode, station);

        var definition = GetDefinition(station.IngredientName);
        var needsPrep = definition.RequiresChop || definition.RequiresCook;

        var ingredient = new Ingredient
        {
            Name = station.IngredientName,
            HeldByConnectionId = connectionId,
            RequiresChop = definition.RequiresChop,
            RequiresCook = definition.RequiresCook,
            // Un ingrediente que no necesita picarse ni cocinarse (ej. leche condensada) queda listo de una vez.
            State = needsPrep ? IngredientState.Crudo : IngredientState.Listo
        };
        var ingredients = _ingredientsByRoom.GetOrAdd(roomCode, _ => new List<Ingredient>());

        lock (ingredients)
        {
            ingredients.Add(ingredient);
        }

        return new TakeIngredientResult { Success = true, Ingredient = ingredient };
    }

    /// <summary>AB#20 - Picar un ingrediente crudo. AB#78: solo si ese ingrediente necesita picarse.</summary>
    public IngredientActionResult ChopIngredient(string roomCode, string ingredientId, string connectionId)
    {
        var ingredient = FindIngredient(roomCode, ingredientId, connectionId);
        if (ingredient is null)
            return new IngredientActionResult { Success = false, Error = "No tienes ese ingrediente." };

        lock (ingredient)
        {
            if (!ingredient.RequiresChop)
                return new IngredientActionResult { Success = false, Error = "Este ingrediente no se pica." };

            if (ingredient.State != IngredientState.Crudo)
                return new IngredientActionResult { Success = false, Error = "El ingrediente ya no está crudo." };

            ingredient.State = IngredientState.Picado;
        }

        return new IngredientActionResult { Success = true };
    }

    /// <summary>
    /// AB#21 - Cocinar un ingrediente. El avance a "cocinado" y el quemado ocurren solos con el tiempo.
    /// AB#78: si el ingrediente no necesita picarse (ej. leche, arroz), se cocina directo desde Crudo.
    /// </summary>
    public IngredientActionResult CookIngredient(string roomCode, string ingredientId, string connectionId)
    {
        var ingredient = FindIngredient(roomCode, ingredientId, connectionId);
        if (ingredient is null)
            return new IngredientActionResult { Success = false, Error = "No tienes ese ingrediente." };

        lock (ingredient)
        {
            if (!ingredient.RequiresCook)
                return new IngredientActionResult { Success = false, Error = "Este ingrediente no se cocina." };

            var estadoValido = ingredient.RequiresChop ? IngredientState.Picado : IngredientState.Crudo;

            if (ingredient.State != estadoValido)
            {
                var mensaje = ingredient.RequiresChop
                    ? "El ingrediente debe estar picado antes de cocinarlo."
                    : "El ingrediente ya no está crudo.";

                return new IngredientActionResult { Success = false, Error = mensaje };
            }

            ingredient.State = IngredientState.Cocinando;
        }

        _ = RunCookingTimerAsync(roomCode, ingredient);

        return new IngredientActionResult { Success = true };
    }

    public IngredientActionResult RemoveFromHeat(
        string roomCode,
        string ingredientId,
        string connectionId)
    {
        var ingredient = FindIngredient(
            roomCode,
            ingredientId,
            connectionId);

        if (ingredient is null)
            return new IngredientActionResult
            {
                Success = false,
                Error = "No tienes ese ingrediente."
            };

        lock (ingredient)
        {
            if (ingredient.State != IngredientState.Cocinado)
                return new IngredientActionResult
                {
                    Success = false,
                    Error = "El ingrediente todavía no está listo para retirar."
                };

            ingredient.State = IngredientState.Listo;
        }

        return new IngredientActionResult
        {
            Success = true
        };
    }

    /// <summary>AB#77 - Tira a la basura un ingrediente quemado y avisa a toda la sala.</summary>
    public async Task<IngredientActionResult> DiscardIngredientAsync(string roomCode, string ingredientId, string connectionId)
    {
        if (!_ingredientsByRoom.TryGetValue(roomCode, out var ingredients))
            return new IngredientActionResult { Success = false, Error = "No tienes ese ingrediente." };

        lock (ingredients)
        {
            var ingredient = ingredients.FirstOrDefault(i => i.Id == ingredientId && i.HeldByConnectionId == connectionId);

            if (ingredient is null)
                return new IngredientActionResult { Success = false, Error = "No tienes ese ingrediente." };

            if (ingredient.State != IngredientState.Quemado)
                return new IngredientActionResult { Success = false, Error = "Solo puedes tirar a la basura un ingrediente quemado." };

            ingredients.Remove(ingredient);
        }

        await _hub.Clients.Group(roomCode).SendAsync("IngredientDiscarded", new { Id = ingredientId });

        return new IngredientActionResult { Success = true };
    }

    private async Task RestockStationAsync(string roomCode, Station station)
    {
        await Task.Delay(TimeSpan.FromSeconds(RestockSeconds));

        lock (station)
        {
            station.IsAvailable = true;
        }

        var stations = GetStations(roomCode);

        await _hub.Clients.Group(roomCode).SendAsync("StationsUpdated", stations.Select(s => new
        {
            s.Id,
            s.IngredientName,
            s.IsAvailable
        }));
    }

    private async Task RunCookingTimerAsync(string roomCode, Ingredient ingredient)
    {
        await Task.Delay(TimeSpan.FromSeconds(CookSeconds));

        lock (ingredient)
        {
            if (ingredient.State == IngredientState.Cocinando)
                ingredient.State = IngredientState.Cocinado;
        }
        await BroadcastIngredientUpdated(roomCode, ingredient);

        await Task.Delay(TimeSpan.FromSeconds(BurnSeconds - CookSeconds));

        lock (ingredient)
        {
            if (ingredient.State != IngredientState.Cocinado)
                return; // ya lo retiraron a tiempo, no se quema

            ingredient.State = IngredientState.Quemado;
        }
        await BroadcastIngredientUpdated(roomCode, ingredient);
    }

    private async Task BroadcastIngredientUpdated(string roomCode, Ingredient ingredient)
    {
        await _hub.Clients.Group(roomCode).SendAsync("IngredientUpdated", new
        {
            ingredient.Id,
            ingredient.Name,
            State = ingredient.State.ToString()
        });
    }

    /// <summary>
    /// Ingredientes preparados de la sala (sin importar quien los tomo originalmente).
    /// Cualquier jugador de la sala puede usarlos para emplatar. AB#76.
    /// </summary>
    public List<Ingredient> GetRoomIngredients(
        string roomCode,
        IEnumerable<string> ingredientIds)
    {
        if (!_ingredientsByRoom.TryGetValue(roomCode, out var ingredients))
            return new List<Ingredient>();

        var ids = ingredientIds.ToHashSet();

        lock (ingredients)
        {
            return ingredients
                .Where(i => ids.Contains(i.Id))
                .ToList();
        }
    }

    /// <summary>Retira ingredientes del estado compartido de la sala cuando se usan para emplatar. AB#76.</summary>
    public bool RemoveRoomIngredients(
        string roomCode,
        IEnumerable<string> ingredientIds)
    {
        if (!_ingredientsByRoom.TryGetValue(roomCode, out var ingredients))
            return false;

        var ids = ingredientIds.ToHashSet();

        lock (ingredients)
        {
            var selected = ingredients
                .Where(i => ids.Contains(i.Id))
                .ToList();

            if (selected.Count != ids.Count)
                return false;

            foreach (var ingredient in selected)
                ingredients.Remove(ingredient);

            return true;
        }
    }

    private Ingredient? FindIngredient(string roomCode, string ingredientId, string connectionId)
    {
        if (!_ingredientsByRoom.TryGetValue(roomCode, out var ingredients))
            return null;

        lock (ingredients)
        {
            return ingredients.FirstOrDefault(i => i.Id == ingredientId && i.HeldByConnectionId == connectionId);
        }
    }
}
