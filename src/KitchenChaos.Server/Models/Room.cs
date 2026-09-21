namespace KitchenChaos.Server.Models;

public class Player
{
    public required string ConnectionId { get; set; }
    public required string Name { get; set; }
}

public class Room
{
    public required string Code { get; set; }

    // AB#6 - El jugador que crea la sala es el host
    public required string HostConnectionId { get; set; }

    public bool HasStarted { get; set; } = false;

    // AB#78 - Nivel actual de la sala, define que recetas e ingredientes aplican.
    public int Level { get; set; } = 1;

    public List<Player> Players { get; } = new();
}