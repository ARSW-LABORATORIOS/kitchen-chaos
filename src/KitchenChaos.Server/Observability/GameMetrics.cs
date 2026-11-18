using Prometheus;

namespace KitchenChaos.Server.Observability;

/// <summary>
/// Todas las métricas del juego en un solo lugar. Prefijo kc_ (Kitchen Chaos).
/// El resto del código solo llama a estos contadores con una línea, para no esparcir
/// la observabilidad por los servicios.
/// </summary>
public static class GameMetrics
{
    // Buckets en segundos: de 5 ms a 2.5 s, para ver bien tanto lo rápido como lo lento.
    private static readonly double[] LatencyBuckets = { 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5 };

    /// <summary>Duración de cada llamada al GameHub (SignalR), por método.</summary>
    public static readonly Histogram HubInvocationDuration = Metrics.CreateHistogram(
        "kc_hub_invocation_duration_seconds",
        "Duración de cada llamada al GameHub, por método",
        new HistogramConfiguration { LabelNames = new[] { "method" }, Buckets = LatencyBuckets });

    /// <summary>Llamadas al GameHub por método y resultado (ok o error).</summary>
    public static readonly Counter HubInvocations = Metrics.CreateCounter(
        "kc_hub_invocations_total",
        "Llamadas al GameHub por método y resultado",
        new CounterConfiguration { LabelNames = new[] { "method", "status" } });

    /// <summary>Conexiones de jugadores abiertas ahora mismo (incluye celulares vinculados).</summary>
    public static readonly Gauge ConnectedPlayers = Metrics.CreateGauge(
        "kc_connected_players",
        "Conexiones SignalR abiertas");

    /// <summary>Salas que existen ahora mismo.</summary>
    public static readonly Gauge ActiveRooms = Metrics.CreateGauge(
        "kc_active_rooms",
        "Salas activas");

    /// <summary>
    /// Intentos de tomar un ingrediente. result=conflict es la prueba de la concurrencia:
    /// dos jugadores quisieron la misma estación y el lock dejó pasar solo a uno.
    /// </summary>
    public static readonly Counter StationTakes = Metrics.CreateCounter(
        "kc_station_takes_total",
        "Intentos de tomar un ingrediente de una estación",
        new CounterConfiguration { LabelNames = new[] { "result" } });

    /// <summary>Platos entregados, según el pedido sea correcto o no.</summary>
    public static readonly Counter OrdersDelivered = Metrics.CreateCounter(
        "kc_orders_delivered_total",
        "Platos entregados",
        new CounterConfiguration { LabelNames = new[] { "result" } });

    /// <summary>Pedidos que vencieron sin entregarse.</summary>
    public static readonly Counter OrdersExpired = Metrics.CreateCounter(
        "kc_orders_expired_total",
        "Pedidos vencidos sin entregar");

    /// <summary>Salas creadas desde que arrancó el servidor.</summary>
    public static readonly Counter RoomsCreated = Metrics.CreateCounter(
        "kc_rooms_created_total",
        "Salas creadas");

    /// <summary>
    /// Intentos de unirse a una sala. result=full es la sala llena (4/4): el lock del cupo
    /// impide que entre un quinto jugador cuando dos llegan a la vez.
    /// </summary>
    public static readonly Counter RoomJoins = Metrics.CreateCounter(
        "kc_room_joins_total",
        "Intentos de unirse a una sala",
        new CounterConfiguration { LabelNames = new[] { "result" } });

    /// <summary>Pasos de la cocina: picado, cocinado, retirado a tiempo (ready) y quemado.</summary>
    public static readonly Counter IngredientEvents = Metrics.CreateCounter(
        "kc_ingredient_events_total",
        "Pasos de preparación de ingredientes",
        new CounterConfiguration { LabelNames = new[] { "event" } });

    /// <summary>Platos armados con los ingredientes del pedido.</summary>
    public static readonly Counter DishesPlated = Metrics.CreateCounter(
        "kc_dishes_plated_total",
        "Platos emplatados correctamente");

    /// <summary>Partidas iniciadas, por nivel.</summary>
    public static readonly Counter GamesStarted = Metrics.CreateCounter(
        "kc_games_started_total",
        "Partidas iniciadas",
        new CounterConfiguration { LabelNames = new[] { "level" } });
}
