using AdaptiveTrials.Application.DTOs.Players;
using AdaptiveTrials.Application.DTOs.SessionQueries;
using AdaptiveTrials.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AdaptiveTrials.Api.Controllers;

[ApiController]
[Route("api/players")]
public class PlayersController : ControllerBase
{
    private readonly IPlayerService _playerService;
    private readonly ISessionService _sessionService;

    public PlayersController(IPlayerService playerService, ISessionService sessionService)
    {
        _playerService = playerService;
        _sessionService = sessionService;
    }

    [HttpGet("{playerId:int}/profile")]
    public async Task<ActionResult<PlayerProfileResponse>> GetPlayerProfile(int playerId)
    {
        var response = await _playerService.GetPlayerProfileAsync(playerId);

        if (response is null)
        {
            return NotFound(new { message = "Player profile not found." });
        }

        return Ok(response);
    }

    [HttpGet("{playerId:int}/sessions")]
    public async Task<ActionResult<List<PlayerSessionSummaryResponse>>> GetPlayerSessions(
        int playerId
    )
    {
        var response = await _sessionService.GetPlayerSessionsAsync(playerId);

        if (response is null)
        {
            return NotFound(new { message = "Player not found." });
        }

        return Ok(response);
    }

    [HttpPost("{playerId:int}/preferences")]
    public async Task<ActionResult<PlayerProfileResponse>> RegisterManualPreferences(
        int playerId,
        [FromBody] ManualPreferencesRequest request
    )
    {
        try
        {
            var response = await _playerService.RegisterManualPreferencesAsync(playerId, request);

            if (response is null)
            {
                return NotFound(new { message = "Player not found." });
            }

            return Ok(response);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
