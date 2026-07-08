using System.Text.Json;
using AdaptiveTrials.Application.DTOs.Players;
using AdaptiveTrials.Application.Interfaces;
using AdaptiveTrials.Domain.Entities;
using AdaptiveTrials.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AdaptiveTrials.Infrastructure.Services;

public class PlayerService : IPlayerService
{
    private readonly AppDbContext _context;

    public PlayerService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<PlayerProfileResponse?> RegisterManualPreferencesAsync(
        int playerId,
        ManualPreferencesRequest request
    )
    {
        var player = await _context.Players
            .Include(p => p.NormalizedProfile)
            .FirstOrDefaultAsync(p => p.Id == playerId);

        if (player is null)
        {
            return null;
        }

        ValidatePreferences(request);

        var normalizedPreferences = NormalizePreferences(request);

        player.ManualPreferencesJson = JsonSerializer.Serialize(new
        {
            combat = normalizedPreferences.Combat,
            exploration = normalizedPreferences.Exploration,
            puzzle = normalizedPreferences.Puzzle
        });

        if (player.NormalizedProfile is null)
        {
            player.NormalizedProfile = new NormalizedProfile
            {
                PlayerId = player.Id,
                Source = "manual",
                Combat = normalizedPreferences.Combat,
                Exploration = normalizedPreferences.Exploration,
                Puzzle = normalizedPreferences.Puzzle,
                TotalPlaytime = 0,
                NumGames = 0,
                GamesCombat = 0,
                GamesExploration = 0,
                GamesPuzzle = 0,
                HoursCombat = 0,
                HoursExploration = 0,
                HoursPuzzle = 0,
                AvgPlaytimePerGame = 0,
                Diversity = CalculateDiversity(normalizedPreferences),
                Entropy = CalculateEntropy(normalizedPreferences),
                Dominance = CalculateDominance(normalizedPreferences),
                SecondMax = CalculateSecondMax(normalizedPreferences),
                Gap = CalculateGap(normalizedPreferences),
                CreatedAt = DateTime.UtcNow
            };

            _context.NormalizedProfiles.Add(player.NormalizedProfile);
        }
        else
        {
            player.NormalizedProfile.Source = "manual";
            player.NormalizedProfile.Combat = normalizedPreferences.Combat;
            player.NormalizedProfile.Exploration = normalizedPreferences.Exploration;
            player.NormalizedProfile.Puzzle = normalizedPreferences.Puzzle;
            player.NormalizedProfile.TotalPlaytime = 0;
            player.NormalizedProfile.NumGames = 0;
            player.NormalizedProfile.GamesCombat = 0;
            player.NormalizedProfile.GamesExploration = 0;
            player.NormalizedProfile.GamesPuzzle = 0;
            player.NormalizedProfile.HoursCombat = 0;
            player.NormalizedProfile.HoursExploration = 0;
            player.NormalizedProfile.HoursPuzzle = 0;
            player.NormalizedProfile.AvgPlaytimePerGame = 0;
            player.NormalizedProfile.Diversity = CalculateDiversity(normalizedPreferences);
            player.NormalizedProfile.Entropy = CalculateEntropy(normalizedPreferences);
            player.NormalizedProfile.Dominance = CalculateDominance(normalizedPreferences);
            player.NormalizedProfile.SecondMax = CalculateSecondMax(normalizedPreferences);
            player.NormalizedProfile.Gap = CalculateGap(normalizedPreferences);
        }

        await _context.SaveChangesAsync();

        return new PlayerProfileResponse
        {
            PlayerId = player.Id,
            Source = player.NormalizedProfile.Source,
            Combat = player.NormalizedProfile.Combat,
            Exploration = player.NormalizedProfile.Exploration,
            Puzzle = player.NormalizedProfile.Puzzle,
            CreatedAt = player.NormalizedProfile.CreatedAt
        };
    }

    private static void ValidatePreferences(ManualPreferencesRequest request)
    {
        if (request.Combat < 0 || request.Exploration < 0 || request.Puzzle < 0)
        {
            throw new InvalidOperationException("Preferences cannot be negative.");
        }

        var total = request.Combat + request.Exploration + request.Puzzle;

        if (total <= 0)
        {
            throw new InvalidOperationException("At least one preference must be greater than zero.");
        }
    }

    private static ManualPreferencesRequest NormalizePreferences(ManualPreferencesRequest request)
    {
        var total = request.Combat + request.Exploration + request.Puzzle;

        return new ManualPreferencesRequest
        {
            Combat = request.Combat / total,
            Exploration = request.Exploration / total,
            Puzzle = request.Puzzle / total
        };
    }

    private static int CalculateDiversity(ManualPreferencesRequest preferences)
    {
        var values = new[]
        {
            preferences.Combat,
            preferences.Exploration,
            preferences.Puzzle
        };

        return values.Count(value => value > 0);
    }

    private static double CalculateEntropy(ManualPreferencesRequest preferences)
    {
        var values = new[]
        {
            preferences.Combat,
            preferences.Exploration,
            preferences.Puzzle
        };

        return values
            .Where(value => value > 0)
            .Sum(value => -value * Math.Log(value));
    }

    private static double CalculateDominance(ManualPreferencesRequest preferences)
    {
        return new[]
        {
            preferences.Combat,
            preferences.Exploration,
            preferences.Puzzle
        }.Max();
    }

    private static double CalculateSecondMax(ManualPreferencesRequest preferences)
    {
        return new[]
        {
            preferences.Combat,
            preferences.Exploration,
            preferences.Puzzle
        }
        .OrderByDescending(value => value)
        .Skip(1)
        .First();
    }

    private static double CalculateGap(ManualPreferencesRequest preferences)
    {
        return CalculateDominance(preferences) - CalculateSecondMax(preferences);
    }
}