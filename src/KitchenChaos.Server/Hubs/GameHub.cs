using KitchenChaos.Server.Services;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Security.Cryptography;

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
    private readonly TimerService _timerService;
    private static readonly ConcurrentDictionary<string, (string ConnectionId, DateTime ExpiresAt)> ControlCodes = new();
    private static readonly ConcurrentDictionary<string, string> LinkedControls = new();
    private static readonly ConcurrentDictionary<string, string> PlayerControls = new();
    private static readonly ConcurrentDictionary<string, DateTime> ControlAttempts = new();

    public GameHub(RoomService roomService, PlayerProfileService profileService, PreparationService preparationService, OrderService orderService, PlatingService platingService, DeliveryService deliveryService, TimerService timerService)
    {
        _roomService = roomService;
        _profileService = profileService;
        _preparationService = preparationService;
        _orderService = orderService;
        _platingService = platingService;
        _deliveryService = deliveryService;
        _timerService = timerService;
    }

    // AB#4 - Crear sala de juego
    public async Task<string> CreateRoom(string playerName, string avatarId)
    {
        var room = _roomService.CreateRoom(Context.ConnectionId, playerName, avatarId);
        await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);

        var players = room.Players.Select(p => new { p.Name, p.AvatarId, p.ConnectionId }).ToList();
        await Clients.Group(room.Code).SendAsync("RoomPlayersUpdated", players);

        return room.Code;
    }

    // AB#5 - Unirse a sala con código
    public async Task JoinRoom(string roomCode, string playerName, string avatarId)
    {
        var result = _roomService.JoinRoom(roomCode, Context.ConnectionId, playerName, avatarId);

        if (!result.Success)
        {
            await Clients.Caller.SendAsync("JoinRoomError", result.Error);
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

        var players = result.Room!.Players.Select(p => new { p.Name, p.AvatarId, p.ConnectionId }).ToList();
        await Clients.Group(roomCode).SendAsync("RoomPlayersUpdated", players);
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
    
    // Avanza al siguiente nivel sin destruir la sala
    public async Task NextLevel(string roomCode)
    {
        var room = _roomService.GetRoom(roomCode);
        if (room is null || room.HostConnectionId != Context.ConnectionId)
        {
            await Clients.Caller.SendAsync("StartGameError", "Solo el host puede avanzar de nivel.");
            return;
        }

        var nextLevel = room.Level + 1;
        if (nextLevel > RoomService.MaxLevel)
        {
            await Clients.Caller.SendAsync("StartGameError", "Ya estás en el último nivel.");
            return;
        }

        room.Level = nextLevel;
        _preparationService.ResetStations(roomCode);
        _timerService.StartLevel(roomCode);

        await Clients.Group(roomCode).SendAsync("GameStarted", new { RoomCode = roomCode, Level = nextLevel });

        var order = _orderService.GenerateOrder(roomCode);
        await Clients.Group(roomCode).SendAsync("OrderUpdated", new
        {
            order.Id,
            order.DishName,
            order.RequiredIngredients
        });
    }

    public async Task PauseGame(string roomCode)
    {
        // Puede venir del jugador directo o de su control móvil vinculado
        var callerConnectionId = Context.ConnectionId;
        if (LinkedControls.TryGetValue(callerConnectionId, out var playerConnId))
            callerConnectionId = playerConnId;

        var room = _roomService.GetRoom(roomCode);
        if (room is null || !room.Players.Any(p => p.ConnectionId == callerConnectionId)) return;
        await Clients.Group(roomCode).SendAsync("GamePaused", new { By = callerConnectionId });
    }

    public async Task ResumeGame(string roomCode)
    {
        var callerConnectionId = Context.ConnectionId;
        if (LinkedControls.TryGetValue(callerConnectionId, out var playerConnId))
            callerConnectionId = playerConnId;

        var room = _roomService.GetRoom(roomCode);
        if (room is null || !room.Players.Any(p => p.ConnectionId == callerConnectionId)) return;
        await Clients.Group(roomCode).SendAsync("GameResumed");
    }

    // AB#6 - El host inicia la partida
    public async Task StartGame(string roomCode, int level)
    {
        var result = _roomService.StartGame(
            roomCode,
            Context.ConnectionId,
            level
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
                Level = level
            }
        );

        // AB#79: arranca temporizadores de nivel y pedido controlados por el servidor
        _timerService.StartLevel(roomCode);

        // Limpia estaciones cacheadas para que se regeneren con el nivel correcto
        _preparationService.ResetStations(roomCode);

        var order = _orderService.GenerateOrder(roomCode);
        await Clients.Group(roomCode).SendAsync("OrderUpdated", new
        {
            order.Id,
            order.DishName,
            order.RequiredIngredients
        });
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
            State = result.Ingredient.State.ToString(),
            result.Ingredient.RequiresChop,
            result.Ingredient.RequiresCook,
            CookMethod = result.Ingredient.CookMethod.ToString()
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

    /// <summary>Retira un ingrediente cocinado de la estufa antes de que se queme. AB#21.</summary>
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

    public async Task GenerateControlCode(string roomCode)
    {
        var room = _roomService.GetRoom(roomCode);

        if (room is null ||
            !room.Players.Any(p => p.ConnectionId == Context.ConnectionId))
        {
            throw new HubException("No perteneces a esta sala.");
        }

        foreach (var entry in ControlCodes)
        {
            if (entry.Value.ConnectionId == Context.ConnectionId)
            {
                ControlCodes.TryRemove(entry.Key, out _);
            }
        }

        string code;

        do
        {
            code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        }
        while (!ControlCodes.TryAdd(
            code,
            (Context.ConnectionId, DateTime.UtcNow.AddMinutes(2))
        ));

        await Clients.Caller.SendAsync("ControlCodeGenerated", new
        {
            Code = code,
            ExpiresInSeconds = 120
        });
    }

    public async Task LinkMobileControl(string code)
    {
        if (string.IsNullOrWhiteSpace(code) ||
            !System.Text.RegularExpressions.Regex.IsMatch(code, @"^\d{6}$"))
        {
            throw new HubException("El código debe tener seis dígitos.");
        }

        var mobileConnectionId = Context.ConnectionId;

        if (ControlAttempts.TryGetValue(mobileConnectionId, out var lastAttempt) &&
            DateTime.UtcNow - lastAttempt < TimeSpan.FromSeconds(2))
        {
            throw new HubException("Espera antes de intentar nuevamente.");
        }

        ControlAttempts[mobileConnectionId] = DateTime.UtcNow;

        if (!ControlCodes.TryRemove(code, out var entry) || entry.ExpiresAt <= DateTime.UtcNow)
        {
            throw new HubException("El código es inválido o ha expirado.");
        }

        var room = _roomService.GetRoomByPlayerConnectionId(entry.ConnectionId);

        if (room is null)
        {
            throw new HubException("El jugador ya no está conectado.");
        }

        if (PlayerControls.TryGetValue(entry.ConnectionId, out var previousControl))
        {
            LinkedControls.TryRemove(previousControl, out _);
            await Clients.Client(previousControl).SendAsync("MobileControlUnlinked");
        }

        LinkedControls[mobileConnectionId] = entry.ConnectionId;
        PlayerControls[entry.ConnectionId] = mobileConnectionId;

        var player = room.Players.First(p => p.ConnectionId == entry.ConnectionId);

        await Clients.Caller.SendAsync("MobileControlLinked", new
        {
            PlayerName = player.Name,
            AvatarId = player.AvatarId,
            RoomCode = room.Code
        });

        await Clients.Client(entry.ConnectionId).SendAsync("MobileControllerConnected");
    }

    
    public async Task SendMobileChop(string ingredientId)
    {
        if (!LinkedControls.TryGetValue(Context.ConnectionId, out var playerConnectionId))
        {
            throw new HubException("Este control no está vinculado.");
        }
        if (!PlayerControls.TryGetValue(playerConnectionId, out var activeControlId) ||
            activeControlId != Context.ConnectionId)
        {
            throw new HubException("Este control ya no está autorizado.");
        }
        var room = _roomService.GetRoomByPlayerConnectionId(playerConnectionId);
        if (room is null || !room.HasStarted)
        {
            throw new HubException("El jugador no está en una partida activa.");
        }
        if (string.IsNullOrWhiteSpace(ingredientId))
        {
            throw new HubException("Ingrediente no válido.");
        }
        await Clients.Client(playerConnectionId)
            .SendAsync("MobileChopReceived", ingredientId);
    }


    public async Task SendMobileDirection(string direction, bool pressed)
    {
        if (!LinkedControls.TryGetValue(Context.ConnectionId, out var playerConnectionId))
        {
            throw new HubException("Este control no está vinculado.");
        }

        if (!PlayerControls.TryGetValue(playerConnectionId, out var activeControlId) ||
            activeControlId != Context.ConnectionId)
        {
            throw new HubException("Este control ya no está autorizado.");
        }

        var validDirections = new[] { "up", "down", "left", "right" };

        if (!validDirections.Contains(direction))
        {
            throw new HubException("Dirección no válida.");
        }

        var room = _roomService.GetRoomByPlayerConnectionId(playerConnectionId);

        if (room is null || !room.HasStarted)
        {
            throw new HubException("El jugador no está en una partida activa.");
        }

        await Clients.Client(playerConnectionId).SendAsync("MobileDirectionReceived", new
        {
            Direction = direction,
            Pressed = pressed
        });
    }

    public async Task SendMobileInteraction()
    {
        if (!LinkedControls.TryGetValue(Context.ConnectionId, out var playerConnectionId))
        {
            throw new HubException("Este control no está vinculado.");
        }

        if (!PlayerControls.TryGetValue(playerConnectionId, out var activeControlId) ||
            activeControlId != Context.ConnectionId)
        {
            throw new HubException("Este control ya no está autorizado.");
        }

        var room = _roomService.GetRoomByPlayerConnectionId(playerConnectionId);

        if (room is null || !room.HasStarted)
        {
            throw new HubException("El jugador no está en una partida activa.");
        }

        await Clients.Client(playerConnectionId).SendAsync("MobileInteractionReceived");
    }

    
    public record MobileIngredientInfo(
        string Id,
        string Name,
        string State,
        bool Selected,
        bool RequiresChop
    );

    public async Task SyncMobileInventory(List<MobileIngredientInfo> ingredients)
    {
        var room = _roomService.GetRoomByPlayerConnectionId(Context.ConnectionId);

        if (room is null)
        {
            throw new HubException("El jugador no pertenece a una sala.");
        }

        if (!PlayerControls.TryGetValue(Context.ConnectionId, out var mobileConnectionId))
        {
            return;
        }

        if (!LinkedControls.TryGetValue(mobileConnectionId, out var playerConnectionId) ||
            playerConnectionId != Context.ConnectionId)
        {
            return;
        }

        if (ingredients is null || ingredients.Count > 50)
        {
            throw new HubException("Inventario no válido.");
        }

        await Clients.Client(mobileConnectionId)
            .SendAsync("MobileInventoryUpdated", ingredients);
    }

    public async Task SelectMobileIngredient(string ingredientId, bool selected)
    {
        if (!LinkedControls.TryGetValue(Context.ConnectionId, out var playerConnectionId))
        {
            throw new HubException("Este control no está vinculado.");
        }

        if (!PlayerControls.TryGetValue(playerConnectionId, out var activeControlId) ||
            activeControlId != Context.ConnectionId)
        {
            throw new HubException("Este control ya no está autorizado.");
        }

        var room = _roomService.GetRoomByPlayerConnectionId(playerConnectionId);

        if (room is null || !room.HasStarted)
        {
            throw new HubException("El jugador no está en una partida activa.");
        }

        if (string.IsNullOrWhiteSpace(ingredientId))
        {
            throw new HubException("Ingrediente no válido.");
        }

        await Clients.Client(playerConnectionId).SendAsync("MobileIngredientSelectionReceived", new
        {
            IngredientId = ingredientId,
            Selected = selected
        });
    }


    
    public async Task SyncMobilePlate(string? plateState)
    {
        var room = _roomService.GetRoomByPlayerConnectionId(Context.ConnectionId);

        if (room is null)
        {
            throw new HubException("El jugador no pertenece a una sala.");
        }

        if (!PlayerControls.TryGetValue(Context.ConnectionId, out var mobileConnectionId))
        {
            return;
        }

        if (!LinkedControls.TryGetValue(mobileConnectionId, out var playerConnectionId) ||
            playerConnectionId != Context.ConnectionId)
        {
            return;
        }

        if (plateState is not null &&
            plateState != "clean" &&
            plateState != "food" &&
            plateState != "dirty")
        {
            throw new HubException("Estado de plato no válido.");
        }

        await Clients.Client(mobileConnectionId)
            .SendAsync("MobilePlateUpdated", plateState);
    }
  
    public async Task SyncMobileScore(int score)
    {
        var room = _roomService.GetRoomByPlayerConnectionId(Context.ConnectionId);

        if (room is null)
        {
            throw new HubException("El jugador no pertenece a una sala.");
        }

        if (!PlayerControls.TryGetValue(Context.ConnectionId, out var mobileConnectionId))
        {
            return;
        }

        if (!LinkedControls.TryGetValue(mobileConnectionId, out var playerConnectionId) ||
            playerConnectionId != Context.ConnectionId)
        {
            return;
        }

        await Clients.Client(mobileConnectionId)
            .SendAsync("MobileScoreUpdated", score);
    }

    public record MobileTeammateInfo(string Name, string AvatarId);
    public async Task SyncMobileTeammates(List<MobileTeammateInfo> teammates)
    {
        var room = _roomService.GetRoomByPlayerConnectionId(Context.ConnectionId);
        if (room is null)
        {
            throw new HubException("El jugador no pertenece a una sala.");
        }
        if (!PlayerControls.TryGetValue(Context.ConnectionId, out var mobileConnectionId))
        {
            return;
        }
        if (!LinkedControls.TryGetValue(mobileConnectionId, out var playerConnectionId) ||
            playerConnectionId != Context.ConnectionId)
        {
            return;
        }
        if (teammates is null || teammates.Count > 20)
        {
            throw new HubException("Lista de compañeros no válida.");
        }
        await Clients.Client(mobileConnectionId)
            .SendAsync("MobileTeammatesUpdated", teammates);
    }

    /// <summary>
    /// Retransmite la posición del jugador a los demás de la sala. AB#81.
    /// Puramente visual: no valida colisiones en el servidor, cada cliente ya las calculó localmente.
    /// </summary>
    public async Task MovePlayer(string roomCode, double x, double y, bool facingLeft)
    {
        await Clients.OthersInGroup(roomCode).SendAsync("PlayerMoved", new
        {
            ConnectionId = Context.ConnectionId,
            X = x,
            Y = y,
            FacingLeft = facingLeft
        });
    }

    // AB#77 - Tirar a la basura un ingrediente quemado
    public async Task DiscardIngredient(string roomCode, string ingredientId)
    {
        var result = await _preparationService.DiscardIngredientAsync(roomCode, ingredientId, Context.ConnectionId);

        if (!result.Success)
        {
            await Clients.Caller.SendAsync("DiscardIngredientError", result.Error);
        }
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

    public async Task TakePlate(string roomCode)
    {
        var room = _roomService.GetRoom(roomCode);

        if (room is null ||
            !room.HasStarted ||
            !room.Players.Any(p => p.ConnectionId == Context.ConnectionId))
        {
            await Clients.Caller.SendAsync(
                "TakePlateError",
                "No perteneces a una partida activa en esta sala."
            );
            return;
        }

        var result = _platingService.TakePlate(
            roomCode,
            Context.ConnectionId
        );

        if (!result.Success)
        {
            await Clients.Caller.SendAsync(
                "TakePlateError",
                result.Error
            );
            return;
        }

        await Clients.Group(roomCode).SendAsync(
            "PlateTaken",
            new
            {
                ConnectionId = Context.ConnectionId,
                PlateId = result.Plate!.Id,
                State = result.Plate.State.ToString()
            }
        );
    }

    /// <summary>Arma un plato con los ingredientes indicados y lo deja listo para entregar. AB#22, AB#76.</summary>
    public async Task PlateDish(
        string roomCode,
        List<string> ingredientIds)
    {
        var result = _platingService.PlateDish(
            roomCode,
            ingredientIds,
            Context.ConnectionId);

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
                ConnectionId = Context.ConnectionId,
                result.Plate!.Id,
                result.Plate.DishName,
                result.Plate.Ingredients,
                State = result.Plate.State.ToString()
            });
    }

    
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context.ConnectionId;
        ControlAttempts.TryRemove(connectionId, out _);
        foreach (var entry in ControlCodes)
        {
            if (entry.Value.ConnectionId == connectionId)
            {
                ControlCodes.TryRemove(entry.Key, out _);
            }
        }
        if (LinkedControls.TryRemove(connectionId, out var playerConnectionId))
        {
            if (PlayerControls.TryGetValue(playerConnectionId, out var activeControlId) &&
                activeControlId == connectionId)
            {
                PlayerControls.TryRemove(playerConnectionId, out _);
                await Clients.Client(playerConnectionId).SendAsync("MobileControlDisconnected");
            }
        }
        if (PlayerControls.TryRemove(connectionId, out var mobileConnectionId))
        {
            if (LinkedControls.TryGetValue(mobileConnectionId, out var linkedPlayerId) &&
                linkedPlayerId == connectionId)
            {
                LinkedControls.TryRemove(mobileConnectionId, out _);
                await Clients.Client(mobileConnectionId).SendAsync("MobileControlUnlinked");
            }
        }
        var room = _roomService.RemovePlayer(connectionId);
        if (room != null)
        {
            await Clients.Group(room.Code)
                .SendAsync("PlayerLeft", new { ConnectionId = connectionId });
            if (room.Players.Count > 0)
            {
                var players = room.Players.Select(p => new
                {
                    p.Name,
                    p.AvatarId,
                    p.ConnectionId
                }).ToList();
                await Clients.Group(room.Code)
                    .SendAsync("RoomPlayersUpdated", players);
            }
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
                ConnectionId = Context.ConnectionId,
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
                ConnectionId = Context.ConnectionId,
                result.Plate!.Id,
                State = result.Plate.State.ToString()
            });

        // AB#79: suma +100 y reinicia temporizador de pedido si la entrega fue correcta
        if (result.Correct)
            _timerService.OnOrderDelivered(roomCode);

        await Clients.Group(roomCode).SendAsync(
            "OrderDelivered",
            new
            {
                Success = result.Correct,
                Score   = _timerService.GetScore(roomCode)
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
