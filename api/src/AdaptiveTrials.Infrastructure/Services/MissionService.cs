using AdaptiveTrials.Application.DTOs.Missions;
using AdaptiveTrials.Application.Interfaces;
using AdaptiveTrials.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AdaptiveTrials.Infrastructure.Services;

public class MissionService : IMissionService
{
    private readonly AppDbContext _context;

    public MissionService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<MissionResponse>> GetAllMissionsAsync()
    {
        return await _context.Missions
            .AsNoTracking()
            .OrderBy(m => m.Id)
            .Select(m => new MissionResponse
            {
                Id = m.Id,
                Name = m.Name,
                Type = m.Type,
                Template = m.Template,
                Difficulty = m.Difficulty,
                ParametersJson = m.ParametersJson,
                Description = m.Description
            })
            .ToListAsync();
    }

    public async Task<MissionResponse?> GetMissionByIdAsync(int missionId)
    {
        return await _context.Missions
            .AsNoTracking()
            .Where(m => m.Id == missionId)
            .Select(m => new MissionResponse
            {
                Id = m.Id,
                Name = m.Name,
                Type = m.Type,
                Template = m.Template,
                Difficulty = m.Difficulty,
                ParametersJson = m.ParametersJson,
                Description = m.Description
            })
            .FirstOrDefaultAsync();
    }
}