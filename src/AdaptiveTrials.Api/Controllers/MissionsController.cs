using AdaptiveTrials.Application.DTOs.Missions;
using AdaptiveTrials.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AdaptiveTrials.Api.Controllers;

[ApiController]
[Route("api/missions")]
public class MissionsController : ControllerBase
{
    private readonly IMissionService _missionService;

    public MissionsController(IMissionService missionService)
    {
        _missionService = missionService;
    }

    [HttpGet]
    public async Task<ActionResult<List<MissionResponse>>> GetAllMissions()
    {
        var missions = await _missionService.GetAllMissionsAsync();

        return Ok(missions);
    }

    [HttpGet("{missionId:int}")]
    public async Task<ActionResult<MissionResponse>> GetMissionById(int missionId)
    {
        var mission = await _missionService.GetMissionByIdAsync(missionId);

        if (mission is null)
        {
            return NotFound(new
            {
                message = "Mission not found."
            });
        }

        return Ok(mission);
    }
}