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

    private static readonly string[] StationIngredients =
        ["lechuga", "tomate", "pan", "carne", "queso", "zanahoria", "papa", "caldo"];

    // Estaciones fijas por sala.
    private readonly ConcurrentDictionary<string, List<Station>> _stationsByRoom = new();

    // Ingredientes en preparación por sala (en mano de algún jugador).
    private readonly ConcurrentDictionary<string, List<Ingredient>> _ingredientsByRoom = new();

    private readonly IHubContext<GameHub> _hub;

    public PreparationService(IHubContext<GameHub> hub)
    {
        _hub = hub;
    }

    /// <summary>AB#19 - Estaciones de la sala (se crean la primera vez que se piden).</summary>
    public List<Station> GetStations(string roomCode) =>
        _stationsByRoom.GetOrAdd(roomCode, _ =>
            StationIngredients.Select(name => new Station
            {
                Id = Guid.NewGuid().ToString(),
                IngredientName = name
            }).ToList());

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

        var ingredient = new Ingredient { Name = station.IngredientName, HeldByConnectionId = connectionId };
        var ingredients = _ingredientsByRoom.GetOrAdd(roomCode, _ => new List<Ingredient>());

        lock (ingredients)
        {
            ingredients.Add(ingredient);
        }

        return new TakeIngredientResult { Success = true, Ingredient = ingredient };
    }

    /// <summary>AB#20 - Picar un ingrediente crudo.</summary>
    public IngredientActionResult ChopIngredient(string roomCode, string ingredientId, string connectionId)
    {
        var ingredient = FindIngredient(roomCode, ingredientId, connectionId);
        if (ingredient is null)
            return new IngredientActionResult { Success = false, Error = "No tienes ese ingrediente." };

        lock (ingredient)
        {
            if (ingredient.State != IngredientState.Crudo)
                return new IngredientActionResult { Success = false, Error = "El ingrediente ya no está crudo." };

            ingredient.State = IngredientState.Picado;
        }

        return new IngredientActionResult { Success = true };
    }

    /// <summary>AB#21 - Cocinar un ingrediente picado. El avance a "cocinado" y el quemado ocurren solos con el tiempo.</summary>
    public IngredientActionResult CookIngredient(string roomCode, string ingredientId, string connectionId)
    {
        var ingredient = FindIngredient(roomCode, ingredientId, connectionId);
        if (ingredient is null)
            return new IngredientActionResult { Success = false, Error = "No tienes ese ingrediente." };

        lock (ingredient)
        {
            if (ingredient.State != IngredientState.Picado)
                return new IngredientActionResult { Success = false, Error = "El ingrediente debe estar picado antes de cocinarlo." };

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
