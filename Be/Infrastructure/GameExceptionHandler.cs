using Be.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Be.Infrastructure;

/// <summary>Maps <see cref="GameException"/> to RFC 9457 ProblemDetails with a stable <c>code</c> extension.</summary>
public sealed class GameExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not GameException game)
        {
            return false;
        }

        httpContext.Response.StatusCode = game.HttpStatus;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = game.HttpStatus,
                Title = game.Code,
                Detail = game.Message == game.Code ? null : game.Message,
                Extensions = { ["code"] = game.Code },
            },
        });
    }
}
