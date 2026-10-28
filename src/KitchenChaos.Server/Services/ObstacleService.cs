using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using KitchenChaos.Server.Hubs;

namespace KitchenChaos.Server.Services;

/// <summary>
/// Controla las mecánicas de obstáculo por nivel. AB#84
/// Nivel 3: apagón cada 45s durante 3s.
/// Nivel 4: ráfaga de viento cada 15s que empuja a los jugadores.
/// </summary>
public class ObstacleService
{
    private static readonly string[] WindDirections = ["up", "down", "left", "right"];

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _tokens = new();
    private readonly IHubContext<GameHub> _hub;

    public ObstacleService(IHubContext<GameHub> hub)
    {
        _hub = hub;
    }

    /// <summary>Inicia la mecánica de obstáculo del nivel indicado para la sala.</summary>
    public void StartObstacle(string roomCode, int level)
    {
        var cts = new CancellationTokenSource();
        _tokens[roomCode] = cts;

        if (level == 3)
            _ = RunBlackoutLoopAsync(roomCode, cts.Token);
        else if (level == 4)
            _ = RunWindLoopAsync(roomCode, cts.Token);
    }

    /// <summary>Detiene la mecánica de obstáculo de la sala.</summary>
    public void StopObstacle(string roomCode)
    {
        if (_tokens.TryRemove(roomCode, out var cts))
            cts.Cancel();
    }

    // Nivel 3: apagón cada 45s, dura 3s
    private async Task RunBlackoutLoopAsync(string roomCode, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(45), ct);
                await _hub.Clients.Group(roomCode).SendAsync("BlackoutStart", cancellationToken: ct);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                await _hub.Clients.Group(roomCode).SendAsync("BlackoutEnd", cancellationToken: ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    // Nivel 4: ráfaga de viento cada 15s en dirección aleatoria
    private async Task RunWindLoopAsync(string roomCode, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
                var direction = WindDirections[Random.Shared.Next(WindDirections.Length)];
                await _hub.Clients.Group(roomCode).SendAsync("WindGust", new { Direction = direction }, ct);
            }
        }
        catch (OperationCanceledException) { }
    }
}
