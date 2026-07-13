using AdaptiveTrials.Application.DTOs.BehaviorEvents;
using AdaptiveTrials.Application.DTOs.SessionQueries;
using AdaptiveTrials.Application.DTOs.Sessions;
using AdaptiveTrials.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AdaptiveTrials.Api.Controllers;

[ApiController]
[Route("api/sessions")]
public class SessionsController : ControllerBase
{
    private readonly ISessionService _sessionService;
    private readonly IBehaviorEventService _behaviorEventService;

    public SessionsController(
        ISessionService sessionService,
        IBehaviorEventService behaviorEventService
    )
    {
        _sessionService = sessionService;
        _behaviorEventService = behaviorEventService;
    }

    [HttpGet("{sessionId:int}")]
    public async Task<ActionResult<SessionDetailsResponse>> GetSessionById(int sessionId)
    {
        var response = await _sessionService.GetSessionByIdAsync(sessionId);

        if (response is null)
        {
            return NotFound(new
            {
                message = "Session not found."
            });
        }

        return Ok(response);
    }

    [HttpGet("{sessionId:int}/events")]
    public async Task<ActionResult<List<SessionEventResponse>>> GetSessionEvents(int sessionId)
    {
        var response = await _sessionService.GetSessionEventsAsync(sessionId);

        if (response is null)
        {
            return NotFound(new
            {
                message = "Session not found."
            });
        }

        return Ok(response);
    }

    [HttpGet("{sessionId:int}/recommendations")]
    public async Task<ActionResult<List<SessionRecommendationResponse>>> GetSessionRecommendations(int sessionId)
    {
        var response = await _sessionService.GetSessionRecommendationsAsync(sessionId);

        if (response is null)
        {
            return NotFound(new
            {
                message = "Session not found."
            });
        }

        return Ok(response);
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

    [HttpPost("{sessionId:int}/events")]
    public async Task<ActionResult<RegisterBehaviorEventResponse>> RegisterBehaviorEvent(
        int sessionId,
        [FromBody] RegisterBehaviorEventRequest request
    )
    {
        try
        {
            var response = await _behaviorEventService.RegisterEventAsync(sessionId, request);

            if (response is null)
            {
                return NotFound(new
                {
                    message = "Session not found."
                });
            }

            return CreatedAtAction(
                nameof(RegisterBehaviorEvent),
                new { sessionId, eventId = response.EventId },
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
}