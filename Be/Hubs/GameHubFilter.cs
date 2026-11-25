using Be.Domain;
using Microsoft.AspNetCore.SignalR;

namespace Be.Hubs;

/// <summary>Turns <see cref="GameException"/> into a <see cref="HubException"/> whose message is the stable error code.</summary>
public sealed class GameHubFilter(ILogger<GameHubFilter> logger) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(invocationContext);
        }
        catch (GameException ex)
        {
            logger.LogInformation("Hub method {Method} failed with {Code}", invocationContext.HubMethodName, ex.Code);
            throw new HubException(ex.Code);
        }
    }
}
