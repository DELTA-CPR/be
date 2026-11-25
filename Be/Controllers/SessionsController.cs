using Be.Contracts;
using Be.Infrastructure;
using Be.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Be.Controllers;

/// <summary>Session bootstrap. Everything after obtaining a player or admin token happens over SignalR at <c>/hubs/game</c>.</summary>
[ApiController]
[Route("api/sessions")]
[Produces("application/json")]
public sealed class SessionsController(SessionService sessions) : ControllerBase
{
    /// <summary>Creates a session. The caller becomes its admin.</summary>
    /// <response code="201">Session created; use <c>adminToken</c> to connect to the hub.</response>
    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType<CreateSessionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CreateSessionResponse>> Create(CreateSessionRequest? request, CancellationToken cancellationToken = default) =>
        StatusCode(StatusCodes.Status201Created, await sessions.CreateAsync(request, cancellationToken));

    /// <summary>Joins a session by the code from the QR code or typed by the player. Returns a <c>pending</c> token.</summary>
    [HttpPost("join")]
    [AllowAnonymous]
    [ProducesResponseType<JoinSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<JoinSessionResponse>> Join(JoinSessionRequest request, CancellationToken cancellationToken = default) =>
        Ok(await sessions.JoinAsync(request, cancellationToken));

    /// <summary>Links this device to the registered player whose marker was scanned and returns the upgraded token.</summary>
    [HttpPost("{sessionId}/device-link")]
    [Authorize]
    [ProducesResponseType<DeviceLinkResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeviceLinkResponse>> LinkDevice(string sessionId, DeviceLinkRequest request, CancellationToken cancellationToken = default) =>
        Ok(await sessions.LinkDeviceAsync(CallerIdentity.From(User), sessionId, request.MarkerId!.Value, cancellationToken));
}
