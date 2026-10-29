using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;

namespace KitchenChaos.Server.Observability;

/// <summary>
/// Mide todas las llamadas del GameHub sin tocar cada método: SignalR pasa cada invocación
/// por este filtro, y así se obtiene duración, conteo y resultado de los 29 métodos del hub.
/// </summary>
public class MetricsHubFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext context,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var method = context.HubMethodName;
        var status = "ok";
        var timer = Stopwatch.StartNew();

        try
        {
            return await next(context);
        }
        catch
        {
            status = "error";
            throw;
        }
        finally
        {
            timer.Stop();
            GameMetrics.HubInvocationDuration.WithLabels(method).Observe(timer.Elapsed.TotalSeconds);
            GameMetrics.HubInvocations.WithLabels(method, status).Inc();
        }
    }

    public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        GameMetrics.ConnectedPlayers.Inc();
        return next(context);
    }

    public async Task OnDisconnectedAsync(
        HubLifetimeContext context,
        Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
    {
        try
        {
            await next(context, exception);
        }
        finally
        {
            GameMetrics.ConnectedPlayers.Dec();
        }
    }
}
