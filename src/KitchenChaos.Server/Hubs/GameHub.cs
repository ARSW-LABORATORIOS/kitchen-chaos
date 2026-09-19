using KitchenChaos.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace KitchenChaos.Server.Hubs;

/// <summary>
/// Hub principal de SignalR. Canal de comunicación en tiempo real entre servidor y clientes.
/// </summary>
public class GameHub : Hub
{
    private readonly RoomService _roomService;
    private readonly PlayerProfileService _profileService;
    private readonly PreparationService _preparationService;
    private readonly OrderService _orderService;
    private readonly PlatingService _platingService;
    private readonly DeliveryService _deliveryService;

    public GameHub(RoomService roomService, PlayerProfileService profileService, PreparationService preparationService, OrderService orderService, PlatingService platingService, DeliveryService deliveryService)
    {
        _roomService = roomService;
        _profileService = profileService;
        _preparationService = preparationService;
        _orderService = orderService;
        _platingService = platingService;
        _deliveryService = deliveryService;
    }

    // AB#4 - Crear sala de juego
    public async Task<string> CreateRoom(string playerName)
    {
        var room = _roomService.CreateRoom(Context.ConnectionId, playerName);
        await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);

        var playerNames = room.Players.Select(p => p.Name).ToList();
        await Clients.Group(room.Code).SendAsync("RoomPlayersUpdated", playerNames);

        return room.Code;
    }

    // AB#5 - Unirse a sala con código
    public async Task JoinRoom(string roomCode, string playerName)
    {
        var result = _roomService.JoinRoom(roomCode, Context.ConnectionId, playerName);

        if (!result.Success)
        {
            await Clients.Caller.SendAsync("JoinRoomError", result.Error);
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

        var playerNames = result.Room!.Players.Select(p => p.Name).ToList();
        await Clients.Group(roomCode).SendAsync("RoomPlayersUpdated", playerNames);
    }

    /// <summary>
    /// Registra al jugador en la sesión y devuelve su perfil.
    /// Si ya jugó antes y envía su ID previo, reutiliza el perfil existente (AB#13).
    /// Si es nuevo, crea un perfil con ID único (AB#12).
    /// </summary>
    /// <param name="displayName">Nombre visible del jugador.</param>
    /// <param name="existingId">ID previo guardado en el cliente. Null si es la primera vez.</param>
    public async Task RegisterPlayer(string displayName, string? existingId = null)
    {
        var profile = await _profileService.RegisterAsync(displayName, existingId);

        // Solo el cliente que llamó recibe su perfil
        await Clients.Caller.SendAsync("ProfileRegistered", new
        {
            profile.Id,
            profile.DisplayName,
            profile.GamesPlayed
        });
    }

    /// <summary>Reenvía un mensaje a todos los clientes conectados (prueba de conectividad).</summary>
    public async Task SendMessage(string user, string message) =>
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    
    // AB#6 - El host inicia la partida
    public async Task StartGame(string roomCode)
    {
        var result = _roomService.StartGame(
            roomCode,
            Context.ConnectionId
        );

        if (!result.Success)
        {
            await Clients.Caller.SendAsync(
                "StartGameError",
                result.Error
            );

            return;
        }

        // Todos los jugadores de la sala reciben el mismo evento
        await Clients.Group(roomCode).SendAsync(
            "GameStarted",
            new
            {
                RoomCode = roomCode,
                Level = 1
            }
        );
    }

    // AB#19 - Estaciones disponibles en la sala
    public async Task GetStations(string roomCode)
    {
        var stations = _preparationService.GetStations(roomCode);

        await Clients.Caller.SendAsync("StationsUpdated", stations.Select(s => new
        {
            s.Id,
            s.IngredientName,
            s.IsAvailable
        }));
    }

    // AB#19 - Recoger ingrediente de una estación
    public async Task TakeIngredient(string roomCode, string stationId)
    {
        var result = _preparationService.TakeIngredient(roomCode, stationId, Context.ConnectionId);

        if (!result.Success)
        {
            await Clients.Caller.SendAsync("TakeIngredientError", result.Error);
            return;
        }

        await Clients.Caller.SendAsync("IngredientTaken", new
        {
            result.Ingredient!.Id,
            result.Ingredient.Name,
            State = result.Ingredient.State.ToString()
        });

        var stations = _preparationService.GetStations(roomCode);
        await Clients.Group(roomCode).SendAsync("StationsUpdated", stations.Select(s => new
        {
            s.Id,
            s.IngredientName,
            s.IsAvailable
        }));
    }

    // AB#20 - Picar un ingrediente crudo
    public async Task ChopIngredient(string roomCode, string ingredientId)
    {
        var result = _preparationService.ChopIngredient(roomCode, ingredientId, Context.ConnectionId);

        if (!result.Success)
        {
            await Clients.Caller.SendAsync("ChopIngredientError", result.Error);
            return;
        }

        await Clients.Caller.SendAsync("IngredientUpdated", new { Id = ingredientId, State = "Picado" });
    }

    // AB#21 - Cocinar un ingrediente picado (avanza solo con el tiempo)
    public async Task CookIngredient(string roomCode, string ingredientId)
    {
        var result = _preparationService.CookIngredient(roomCode, ingredientId, Context.ConnectionId);

        if (!result.Success)
        {
            await Clients.Caller.SendAsync("CookIngredientError", result.Error);
            return;
        }

        await Clients.Caller.SendAsync("IngredientUpdated", new { Id = ingredientId, State = "Cocinando" });
    }

    /// <summary>
    /// Genera un pedido dinámico para la sala y lo envía a todos los jugadores.
    /// AB#27
    /// </summary>
    /// <param name="roomCode">Código de la sala que solicita el pedido.</param>
    
    public async Task RemoveFromHeat(
        string roomCode,
        string ingredientId)
    {
        var result = _preparationService.RemoveFromHeat(
            roomCode,
            ingredientId,
            Context.ConnectionId);

        if (!result.Success)
        {
            await Clients.Caller.SendAsync(
                "RemoveFromHeatError",
                result.Error);

            return;
        }

        await Clients.Caller.SendAsync(
            "IngredientUpdated",
            new
            {
                Id = ingredientId,
                State = "Listo"
            });
    }

    public async Task GetCurrentOrder(string roomCode)
    {
        var order = _orderService.GetOrCreateActiveOrder(roomCode);

        await Clients.Group(roomCode).SendAsync("OrderUpdated", new
        {
            order.Id,
            order.DishName,
            order.RequiredIngredients
        });
    }

    /// <summary>
    /// Valida el pedido entregado por el equipo.
    /// Si es correcto, genera el siguiente pedido automáticamente.
    /// AB#28
    /// </summary>
    /// <param name="roomCode">Código de la sala que entrega el pedido.</param>
    /// <param name="deliveredIngredients">Ingredientes que el equipo entrega.</param>
    
    public async Task PlateDish(
        string roomCode,
        List<string> ingredientIds)
    {
        var result = _platingService.PlateDish(
            roomCode,
            Context.ConnectionId,
            ingredientIds);

        if (!result.Success)
        {
            await Clients.Caller.SendAsync(
                "PlateDishError",
                result.Error);

            return;
        }

        await Clients.Group(roomCode).SendAsync(
            "DishPlated",
            new
            {
                result.Plate!.Id,
                result.Plate.DishName,
                result.Plate.Ingredients,
                State = result.Plate.State.ToString()
            });
    }

    public async Task DeliverOrder(string roomCode, List<string> deliveredIngredients)
    {
        var correct = _orderService.ValidateDelivery(roomCode, deliveredIngredients);

        if (correct)
        {
            // Pedido correcto: notifica a todos y genera el siguiente automáticamente
            await Clients.Group(roomCode).SendAsync("OrderDelivered", new { Success = true });

            var next = _orderService.GenerateOrder(roomCode);
            await Clients.Group(roomCode).SendAsync("OrderUpdated", new
            {
                next.Id,
                next.DishName,
                next.RequiredIngredients
            });
        }
        else
        {
            // Pedido incorrecto: solo notifica al jugador que entregó
            await Clients.Caller.SendAsync("OrderDelivered", new { Success = false });
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var room = _roomService.RemovePlayer(Context.ConnectionId);

        if (room != null && room.Players.Count > 0)
        {
            var playerNames = room.Players
                .Select(p => p.Name)
                .ToList();

            await Clients.Group(room.Code)
                .SendAsync("RoomPlayersUpdated", playerNames);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task WashPlate(string roomCode, string plateId)
    {
        var result = _platingService.WashPlate(roomCode, plateId);

        if (!result.Success)
        {
            await Clients.Caller.SendAsync(
                "WashPlateError",
                result.Error);

            return;
        }

        await Clients.Group(roomCode).SendAsync(
            "PlateWashed",
            new
            {
                result.Plate!.Id,
                State = result.Plate.State.ToString()
            });
    }

    public async Task DeliverPlate(string roomCode, string plateId)
    {
        var result = _deliveryService.Deliver(roomCode, plateId);

        if (!result.Success)
        {
            await Clients.Caller.SendAsync(
                "DeliverPlateError",
                result.Error);

            return;
        }

        await Clients.Group(roomCode).SendAsync(
            "PlateUpdated",
            new
            {
                result.Plate!.Id,
                State = result.Plate.State.ToString()
            });

        await Clients.Group(roomCode).SendAsync(
            "OrderDelivered",
            new
            {
                Success = result.Correct,
                Score = result.Score
            });

        if (result.Correct)
        {
            var next = _orderService.GenerateOrder(roomCode);

            await Clients.Group(roomCode).SendAsync(
                "OrderUpdated",
                new
                {
                    next.Id,
                    next.DishName,
                    next.RequiredIngredients
                });
        }
    }
}
