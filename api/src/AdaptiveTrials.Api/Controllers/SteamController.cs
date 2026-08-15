using AdaptiveTrials.Application.DTOs.Steam;
using AdaptiveTrials.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AdaptiveTrials.Api.Controllers;

[ApiController]
[Route("api/steam")]
public class SteamController : ControllerBase
{
    private readonly ISteamService _steamService;

    public SteamController(ISteamService steamService)
    {
        _steamService = steamService;
    }

    [HttpPost("import")]
    public async Task<ActionResult<SteamImportResponse>> ImportSteamProfile(
        [FromBody] SteamImportRequest request
    )
    {
        try
        {
            var response = await _steamService.ImportSteamProfileAsync(request);

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
