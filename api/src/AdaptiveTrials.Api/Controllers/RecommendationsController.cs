using AdaptiveTrials.Application.DTOs.Recommendations;
using AdaptiveTrials.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AdaptiveTrials.Api.Controllers;

[ApiController]
[Route("api/recommendations")]
public class RecommendationsController : ControllerBase
{
    private readonly IRecommendationService _recommendationService;

    public RecommendationsController(IRecommendationService recommendationService)
    {
        _recommendationService = recommendationService;
    }

    [HttpPost("next")]
    public async Task<ActionResult<NextRecommendationResponse>> GetNextRecommendation(
        [FromBody] NextRecommendationRequest request
    )
    {
        try
        {
            var response = await _recommendationService.GetNextRecommendationAsync(request);

            if (response is null)
            {
                return NotFound(new
                {
                    message = "Player or session not found."
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

    [HttpGet("behavior-distribution/{sessionId:int}")]
    public async Task<ActionResult<BehaviorDistributionResponse>> GetBehaviorDistribution(int sessionId)
    {
        var response = await _recommendationService.GetBehaviorDistributionAsync(sessionId);

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