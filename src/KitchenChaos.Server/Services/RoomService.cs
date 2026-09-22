using System.Collections.Concurrent;
using KitchenChaos.Server.Models;

namespace KitchenChaos.Server.Services;

public class JoinRoomResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Room? Room { get; set; }
}

public class StartGameResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Room? Room { get; set; }
}

public class RoomService
{
    private const int MaxPlayers = 4;

    // ConcurrentDictionary porque varias salas pueden crearse/consultarse al mismo tiempo
    // desde jugadores distintos, sin que un hilo pise el diccionario de otro.
    private readonly ConcurrentDictionary<string, Room> _rooms = new();

    public Room CreateRoom(string connectionId, string playerName)
    {
        var code = GenerateUniqueCode();
        var room = new Room
        {
            Code = code,
            HostConnectionId = connectionId
        };
        room.Players.Add(new Player { ConnectionId = connectionId, Name = playerName });

        _rooms[code] = room;
        return room;
    }

    public JoinRoomResult JoinRoom(string code, string connectionId, string playerName)
    {
        if (!_rooms.TryGetValue(code, out var room))
        {
            return new JoinRoomResult { Success = false, Error = "El código de sala no existe." };
        }

        // Dos jugadores pueden intentar unirse a la misma sala en el mismo instante.
        // Este lock evita que ambos pasen la validación de cupo y la sala quede con 5 jugadores.
        lock (room)
        {
            if (room.Players.Count >= MaxPlayers)
            {
                return new JoinRoomResult { Success = false, Error = "La sala ya está llena (4/4)." };
            }

            room.Players.Add(new Player { ConnectionId = connectionId, Name = playerName });
        }

        return new JoinRoomResult { Success = true, Room = room };
    }

    public StartGameResult StartGame(string roomCode, string connectionId)
    {
        if (!_rooms.TryGetValue(roomCode, out var room))
        {
            return new StartGameResult
            {
                Success = false,
                Error = "La sala no existe."
            };
        }

        lock (room)
        {
            if (room.HostConnectionId != connectionId)
            {
                return new StartGameResult
                {
                    Success = false,
                    Error = "Solo el host puede iniciar la partida."
                };
            }

            if (room.Players.Count < 2)
            {
                return new StartGameResult
                {
                    Success = false,
                    Error = "Se necesitan al menos 2 jugadores para iniciar."
                };
            }

            if (room.HasStarted)
            {
                return new StartGameResult
                {
                    Success = false,
                    Error = "La partida ya fue iniciada."
                };
            }

            room.HasStarted = true;

            return new StartGameResult
            {
                Success = true,
                Room = room
            };
        }
    }

    public Room? GetRoom(string code) =>
        _rooms.TryGetValue(code, out var room) ? room : null;

    public Room? RemovePlayer(string connectionId)
    {
        foreach (var room in _rooms.Values)
        {
            lock (room)
            {
                var player = room.Players
                    .FirstOrDefault(p => p.ConnectionId == connectionId);

                if (player == null)
                {
                    continue;
                }

                room.Players.Remove(player);

                // Si ya no queda nadie, eliminamos la sala.
                if (room.Players.Count == 0)
                {
                    _rooms.TryRemove(room.Code, out _);
                }

                return room;
            }
        }

        return null;
    }

    private string GenerateUniqueCode()
    {
        string code;
        do
        {
            code = Random.Shared.Next(1000, 9999).ToString();
        } while (_rooms.ContainsKey(code));

        return code;
    }
}
