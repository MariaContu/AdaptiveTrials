using AdaptiveTrials.Application.DTOs.Exports;
using AdaptiveTrials.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AdaptiveTrials.Api.Controllers;

[ApiController]
[Route("api/exports")]
public class ExportsController : ControllerBase
{
    private readonly IExportService _exportService;

    public ExportsController(IExportService exportService)
    {
        _exportService = exportService;
    }

    [HttpGet("sessions")]
    public async Task<ActionResult<List<SessionExportResponse>>> ExportSessions()
    {
        var response = await _exportService.GetSessionsExportAsync();

        return Ok(response);
    }

    [HttpGet("events")]
    public async Task<ActionResult<List<BehaviorEventExportResponse>>> ExportEvents()
    {
        var response = await _exportService.GetBehaviorEventsExportAsync();

        return Ok(response);
    }

    [HttpGet("recommendations")]
    public async Task<ActionResult<List<RecommendationExportResponse>>> ExportRecommendations()
    {
        var response = await _exportService.GetRecommendationsExportAsync();

        return Ok(response);
    }
}
