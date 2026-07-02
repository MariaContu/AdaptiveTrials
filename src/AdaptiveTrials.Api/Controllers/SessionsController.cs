using AdaptiveTrials.Application.DTOs.Sessions;
using AdaptiveTrials.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AdaptiveTrials.Api.Controllers;

[ApiController]
[Route("api/sessions")]
public class SessionsController : ControllerBase
{
    private readonly ISessionService _sessionService;

    public SessionsController(ISessionService sessionService)
    {
        _sessionService = sessionService;
    }

    [HttpPost]
    public async Task<ActionResult<CreateSessionResponse>> CreateSession(
        [FromBody] CreateSessionRequest request)
    {
        try
        {
            var response = await _sessionService.CreateSessionAsync(request);

            return CreatedAtAction(
                nameof(CreateSession),
                new { sessionId = response.SessionId },
                response
            );
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new
            {
                message = exception.Message
            });
        }
    }

    [HttpPost("{sessionId:int}/end")]
    public async Task<ActionResult<EndSessionResponse>> EndSession(int sessionId)
    {
        var response = await _sessionService.EndSessionAsync(sessionId);

        if (response is null)
        {
            return NotFound(new
            {
                message = "Session not found."
            });
        }

        return Ok(response);
    }
}