using AdaptiveTrials.Application.DTOs.Ai;
using AdaptiveTrials.Application.DTOs.Steam;
using AdaptiveTrials.Application.Interfaces;
using AdaptiveTrials.Domain.Entities;
using AdaptiveTrials.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AdaptiveTrials.Infrastructure.Services;

public class SteamService : ISteamService
{
    private readonly AppDbContext _context;
    private readonly IAiPredictionService _aiPredictionService;

    public SteamService(
        AppDbContext context,
        IAiPredictionService aiPredictionService
    )
    {
        _context = context;
        _aiPredictionService = aiPredictionService;
    }

    public async Task<SteamProfilePreviewResponse?> GetSteamProfilePreviewAsync(
        SteamProfilePreviewRequest request
    )
    {
        if (string.IsNullOrWhiteSpace(request.SteamId))
        {
            throw new InvalidOperationException("SteamId is required.");
        }

        var preview = await _aiPredictionService.GetSteamProfilePreviewAsync(request.SteamId);

        if (preview is null
            || !string.Equals(preview.Status, "ok", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(preview.SteamId)
            || string.IsNullOrWhiteSpace(preview.PersonaName))
        {
            throw new InvalidOperationException(
                "Steam profile could not be found or the Steam service is unavailable."
            );
        }

        return new SteamProfilePreviewResponse
        {
            SteamId = preview.SteamId,
            PersonaName = preview.PersonaName,
            AvatarUrl = preview.AvatarFull,
            CountryCode = preview.CountryCode,
            ProfileUrl = preview.ProfileUrl,
            IsCommunityProfilePublic = preview.CommunityVisibilityState == 3
        };
    }

    public async Task<SteamImportResponse?> ImportSteamProfileAsync(SteamImportRequest request)
    {
        var player = await _context
            .Players.Include(player => player.NormalizedProfile)
            .FirstOrDefaultAsync(player => player.Id == request.PlayerId);

        if (player is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.SteamId))
        {
            throw new InvalidOperationException("SteamId is required.");
        }

        var aiProfile = await _aiPredictionService.PredictFromSteamIdAsync(request.SteamId);

        if (aiProfile is not null && IsValidAiProfile(aiProfile))
        {
            player.SteamId = string.IsNullOrWhiteSpace(aiProfile.SteamId)
                ? request.SteamId
                : aiProfile.SteamId;

            CreateOrUpdateProfileFromAi(player, aiProfile);

            await _context.SaveChangesAsync();

            return BuildSteamImportResponse(player, aiProfile.PredictedCategory);
        }

        throw new InvalidOperationException(
            "AI service is unavailable or returned an invalid profile. Use manual preferences instead."
        );
    }

    private static bool IsValidAiProfile(AiPredictionResponse aiProfile)
    {
        if (!string.Equals(aiProfile.Status, "ok", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (aiProfile.Probabilities is null)
        {
            return false;
        }

        var total =
            aiProfile.Probabilities.Combat
            + aiProfile.Probabilities.Exploration
            + aiProfile.Probabilities.StrategicReasoning;

        return total > 0;
    }

    private void CreateOrUpdateProfileFromAi(Player player, AiPredictionResponse aiProfile)
    {
        if (player.NormalizedProfile is null)
        {
            player.NormalizedProfile = new NormalizedProfile
            {
                PlayerId = player.Id,
                CreatedAt = DateTime.UtcNow,
            };

            _context.NormalizedProfiles.Add(player.NormalizedProfile);
        }

        var total =
            aiProfile.Probabilities.Combat
            + aiProfile.Probabilities.Exploration
            + aiProfile.Probabilities.StrategicReasoning;

        if (total <= 0)
        {
            throw new InvalidOperationException("AI service returned invalid probabilities.");
        }

        var combat = aiProfile.Probabilities.Combat / total;
        var exploration = aiProfile.Probabilities.Exploration / total;
        var puzzle = aiProfile.Probabilities.StrategicReasoning / total;

        player.NormalizedProfile.Source = "steam_ai";

        player.NormalizedProfile.Combat = combat;
        player.NormalizedProfile.Exploration = exploration;
        player.NormalizedProfile.Puzzle = puzzle;

        player.NormalizedProfile.TotalPlaytime = aiProfile.LibrarySummary?.TotalPlaytimeHours ?? 0;

        player.NormalizedProfile.NumGames = aiProfile.LibrarySummary?.NumGames ?? 0;

        player.NormalizedProfile.GamesCombat = aiProfile.LibrarySummary?.GamesCombat ?? 0;

        player.NormalizedProfile.GamesExploration = aiProfile.LibrarySummary?.GamesExploration ?? 0;

        player.NormalizedProfile.GamesPuzzle =
            aiProfile.LibrarySummary?.GamesStrategicReasoning ?? 0;

        player.NormalizedProfile.HoursCombat = aiProfile.LibrarySummary?.HoursCombat ?? 0;

        player.NormalizedProfile.HoursExploration = aiProfile.LibrarySummary?.HoursExploration ?? 0;

        player.NormalizedProfile.HoursPuzzle =
            aiProfile.LibrarySummary?.HoursStrategicReasoning ?? 0;

        player.NormalizedProfile.AvgPlaytimePerGame =
            player.NormalizedProfile.NumGames > 0
                ? player.NormalizedProfile.TotalPlaytime / player.NormalizedProfile.NumGames
                : 0;

        player.NormalizedProfile.Diversity = CalculateDiversity(combat, exploration, puzzle);

        player.NormalizedProfile.Entropy = CalculateEntropy(combat, exploration, puzzle);

        player.NormalizedProfile.Dominance = CalculateDominance(combat, exploration, puzzle);

        player.NormalizedProfile.SecondMax = CalculateSecondMax(combat, exploration, puzzle);

        player.NormalizedProfile.Gap =
            player.NormalizedProfile.Dominance.Value - player.NormalizedProfile.SecondMax.Value;
    }

    private static SteamImportResponse BuildSteamImportResponse(
        Player player,
        string predictedCategory
    )
    {
        if (player.NormalizedProfile is null)
        {
            throw new InvalidOperationException("Player profile was not created.");
        }

        return new SteamImportResponse
        {
            PlayerId = player.Id,
            SteamId = player.SteamId ?? string.Empty,
            Source = player.NormalizedProfile.Source,
            PredictedCategory = predictedCategory,

            Combat = player.NormalizedProfile.Combat,
            Exploration = player.NormalizedProfile.Exploration,
            Puzzle = player.NormalizedProfile.Puzzle,

            TotalPlaytime = player.NormalizedProfile.TotalPlaytime,
            NumGames = player.NormalizedProfile.NumGames,

            GamesCombat = player.NormalizedProfile.GamesCombat,
            GamesExploration = player.NormalizedProfile.GamesExploration,
            GamesPuzzle = player.NormalizedProfile.GamesPuzzle,

            HoursCombat = player.NormalizedProfile.HoursCombat,
            HoursExploration = player.NormalizedProfile.HoursExploration,
            HoursPuzzle = player.NormalizedProfile.HoursPuzzle,

            CreatedAt = player.NormalizedProfile.CreatedAt,
        };
    }

    private static int CalculateDiversity(params double[] values)
    {
        return values.Count(value => value > 0);
    }

    private static double CalculateEntropy(params double[] values)
    {
        return values.Where(value => value > 0).Sum(value => -value * Math.Log(value));
    }

    private static double CalculateDominance(params double[] values)
    {
        return values.Max();
    }

    private static double CalculateSecondMax(params double[] values)
    {
        return values.OrderByDescending(value => value).Skip(1).First();
    }

}
