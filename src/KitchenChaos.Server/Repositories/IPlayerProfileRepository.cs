using KitchenChaos.Server.Models;

namespace KitchenChaos.Server.Repositories;

/// <summary>
/// Contrato para persistencia de perfiles de jugador en Cosmos DB.
/// AB#13
/// </summary>
public interface IPlayerProfileRepository
{
    /// <summary>Obtiene un perfil por su ID. Retorna null si no existe.</summary>
    Task<PlayerProfile?> GetByIdAsync(string id);

    /// <summary>Guarda un perfil nuevo. Lanza excepción si el ID ya existe.</summary>
    Task CreateAsync(PlayerProfile profile);

    /// <summary>Reemplaza un perfil existente completo.</summary>
    Task UpdateAsync(PlayerProfile profile);
}
