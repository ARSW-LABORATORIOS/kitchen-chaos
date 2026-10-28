using KitchenChaos.Server.Models;
using KitchenChaos.Server.Repositories;

namespace KitchenChaos.Server.Services;

/// <summary>
/// Lógica de negocio para el perfil de jugador.
/// AB#12: asigna un identificador único válido para toda la partida.
/// AB#13: persiste el perfil en Cosmos DB sin duplicados.
/// </summary>
public class PlayerProfileService
{
    private readonly IPlayerProfileRepository _repository;

    public PlayerProfileService(IPlayerProfileRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Registra un jugador en la sesión.
    /// Si ya existe un perfil con ese ID lo reutiliza; si no, crea uno nuevo.
    /// Cumple AB#12 (identificador único por partida) y AB#13 (sin duplicados en Cosmos DB).
    /// </summary>
    /// <param name="displayName">Nombre visible elegido por el jugador.</param>
    /// <param name="existingId">ID previo del jugador, si ya jugó antes. Null si es la primera vez.</param>
    /// <returns>El perfil activo listo para usar en la partida.</returns>
    public async Task<PlayerProfile> RegisterAsync(string displayName, string? existingId = null)
    {
        if (existingId is not null)
        {
            var existing = await _repository.GetByIdAsync(existingId);
            if (existing is not null)
                return existing; // AB#13: perfil existente → se reutiliza
        }

        // AB#12: perfil nuevo → ID único generado en el modelo
        var profile = new PlayerProfile { DisplayName = displayName };
        await _repository.CreateAsync(profile);
        return profile;
    }

    /// <summary>
    /// Incrementa el contador de partidas jugadas al finalizar una partida.
    /// AB#13: actualiza el perfil existente sin crear uno nuevo.
    /// </summary>
    /// <param name="playerId">ID del jugador cuya partida terminó.</param>
    public async Task RecordGameCompletedAsync(string playerId)
    {
        var profile = await _repository.GetByIdAsync(playerId);
        if (profile is null) return;

        profile.GamesPlayed++;
        await _repository.UpdateAsync(profile);
    }
}
