using AdaptiveTrials.Application.DTOs.Players;
using AdaptiveTrials.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AdaptiveTrials.Api.Controllers;

[ApiController]
[Route("api/players")]
public class PlayersController : ControllerBase
{
    private readonly IPlayerService _playerService;

    public PlayersController(IPlayerService playerService)
    {
        _playerService = playerService;
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
                return NotFound(new
                {
                    message = "Player not found."
                });
            }

            return Ok(response);
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