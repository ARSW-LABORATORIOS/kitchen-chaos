using Newtonsoft.Json;

namespace KitchenChaos.Server.Models;

/// <summary>
/// Representa el perfil de un jugador dentro de una sesión de juego.
/// No requiere autenticación: el identificador se genera automáticamente al unirse.
/// AB#12, AB#13
/// </summary>
public class PlayerProfile
{
    /// <summary>Identificador único del perfil. Usado como partition key en Cosmos DB.</summary>
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Nombre visible del jugador en la sala.</summary>
    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Fecha en que se creó el perfil por primera vez.</summary>
    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Número de partidas completadas por el jugador.</summary>
    [JsonProperty("gamesPlayed")]
    public int GamesPlayed { get; set; } = 0;
}
