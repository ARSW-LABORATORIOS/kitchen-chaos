using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using KitchenChaos.Server.Hubs;

namespace KitchenChaos.Server.Services;

/// <summary>
/// Controla los temporizadores de nivel (300 s) y de pedido (45 s) por sala.
/// El servidor es la única fuente de verdad del tiempo — el front solo muestra
/// lo que este servicio envía. AB#79
/// </summary>
public class TimerService
{
    private const int LevelDurationSeconds  = 300;
    private const int OrderDurationSeconds  = 45;
    private const int OrderExpiredPenalty   = 20;
    private const int OrderDeliveredPoints  = 100;

    /// <summary>Estado de temporizadores activos para una sala.</summary>
    private sealed class RoomTimerState
    {
        public Timer?   LevelTimer        { get; set; }
        public Timer?   OrderTimer        { get; set; }
        public int      SecondsRemaining  { get; set; } = LevelDurationSeconds;
        public int      OrderSecondsLeft  { get; set; } = OrderDurationSeconds;
        public int      Score             { get; set; } = 0;
        public bool     LevelEnded        { get; set; } = false;
    }

    // ConcurrentDictionary: varias salas corren temporizadores en paralelo desde hilos distintos.
    private readonly ConcurrentDictionary<string, RoomTimerState> _states = new();
    private readonly IHubContext<GameHub> _hub;
    private readonly OrderService _orderService;

    public TimerService(IHubContext<GameHub> hub, OrderService orderService)
    {
        _hub          = hub;
        _orderService = orderService;
    }

    /// <summary>
    /// Inicia el temporizador de nivel y el primer temporizador de pedido para la sala.
    /// Si ya existe un temporizador activo para esa sala, no hace nada.
    /// AB#79
    /// </summary>
    /// <param name="roomCode">Código de la sala.</param>
    public void StartLevel(string roomCode)
    {
        if (_states.ContainsKey(roomCode)) return;

        var state = new RoomTimerState();
        _states[roomCode] = state;

        // Tick cada segundo para notificar el tiempo restante del nivel
        state.LevelTimer = new Timer(
            async _ => await OnLevelTickAsync(roomCode),
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1)
        );

        StartOrderTimer(roomCode, state);
    }

    /// <summary>
    /// Suma puntos al puntaje de la sala por entrega correcta (+100).
    /// Reinicia el temporizador de pedido.
    /// AB#79
    /// </summary>
    /// <param name="roomCode">Código de la sala.</param>
    public void OnOrderDelivered(string roomCode)
    {
        if (!_states.TryGetValue(roomCode, out var state) || state.LevelEnded) return;

        lock (state)
        {
            state.Score += OrderDeliveredPoints;
            ResetOrderTimer(roomCode, state);
        }
    }

    /// <summary>Devuelve el puntaje actual de la sala.</summary>
    public int GetScore(string roomCode) =>
        _states.TryGetValue(roomCode, out var s) ? s.Score : 0;

    /// <summary>Detiene y elimina todos los temporizadores de la sala.</summary>
    public void StopLevel(string roomCode)
    {
        if (!_states.TryRemove(roomCode, out var state)) return;
        state.LevelTimer?.Dispose();
        state.OrderTimer?.Dispose();
    }

    // ── Privados ────────────────────────────────────────────────────────────

    private void StartOrderTimer(string roomCode, RoomTimerState state)
    {
        state.OrderSecondsLeft = OrderDurationSeconds;
        state.OrderTimer?.Dispose();
        state.OrderTimer = new Timer(
            async _ => await OnOrderTickAsync(roomCode),
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1)
        );
    }

    private void ResetOrderTimer(string roomCode, RoomTimerState state) =>
        StartOrderTimer(roomCode, state);

    private async Task OnLevelTickAsync(string roomCode)
    {
        if (!_states.TryGetValue(roomCode, out var state)) return;

        int remaining;
        lock (state)
        {
            state.SecondsRemaining--;
            remaining = state.SecondsRemaining;
        }

        // Notifica el tiempo de nivel a todos los jugadores de la sala
        await _hub.Clients.Group(roomCode).SendAsync("LevelTimerTick", new
        {
            SecondsRemaining = remaining
        });

        if (remaining <= 0)
            await EndLevelAsync(roomCode, state);
    }

    private async Task OnOrderTickAsync(string roomCode)
    {
        if (!_states.TryGetValue(roomCode, out var state) || state.LevelEnded) return;

        int remaining;
        lock (state)
        {
            state.OrderSecondsLeft--;
            remaining = state.OrderSecondsLeft;
        }

        // Notifica el tiempo del pedido activo a todos los jugadores
        await _hub.Clients.Group(roomCode).SendAsync("OrderTimerTick", new
        {
            SecondsRemaining = remaining
        });

        if (remaining <= 0)
            await OnOrderExpiredAsync(roomCode, state);
    }

    private async Task OnOrderExpiredAsync(string roomCode, RoomTimerState state)
    {
        lock (state)
        {
            if (state.LevelEnded) return;
            state.Score = Math.Max(0, state.Score - OrderExpiredPenalty);
            ResetOrderTimer(roomCode, state);
        }

        // Genera pedido nuevo automáticamente
        var next = _orderService.GenerateOrder(roomCode);

        await _hub.Clients.Group(roomCode).SendAsync("OrderExpired", new
        {
            Penalty  = OrderExpiredPenalty,
            Score    = state.Score
        });

        await _hub.Clients.Group(roomCode).SendAsync("OrderUpdated", new
        {
            next.Id,
            next.DishName,
            next.RequiredIngredients
        });
    }

    private async Task EndLevelAsync(string roomCode, RoomTimerState state)
    {
        lock (state)
        {
            if (state.LevelEnded) return;
            state.LevelEnded = true;
        }

        state.LevelTimer?.Dispose();
        state.OrderTimer?.Dispose();

        await _hub.Clients.Group(roomCode).SendAsync("LevelEnded", new
        {
            FinalScore = state.Score
        });
    }
}
