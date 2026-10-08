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

    [HttpPost("preview")]
    public async Task<ActionResult<SteamProfilePreviewResponse>> GetSteamProfilePreview(
        [FromBody] SteamProfilePreviewRequest request
    )
    {
        try
        {
            var response = await _steamService.GetSteamProfilePreviewAsync(request);

            if (response is null)
            {
                return NotFound(new { message = "Steam profile not found." });
            }

            return Ok(response);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
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
