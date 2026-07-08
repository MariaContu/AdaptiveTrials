using AdaptiveTrials.Application.DTOs.Missions;

namespace AdaptiveTrials.Application.Interfaces;

public interface IMissionService
{
    Task<List<MissionResponse>> GetAllMissionsAsync();

    Task<MissionResponse?> GetMissionByIdAsync(int missionId);
}