using AdaptiveTrials.Application.DTOs.Ai;
using AdaptiveTrials.Application.DTOs.Steam;
using AdaptiveTrials.Application.Interfaces;
using AdaptiveTrials.Domain.Entities;
using AdaptiveTrials.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AdaptiveTrials.Infrastructure.Services;

public class SteamService : ISteamService
{
    private readonly AppDbContext _context;
    private readonly IAiPredictionService _aiPredictionService;
    private readonly IConfiguration _configuration;

    public SteamService(
        AppDbContext context,
        IAiPredictionService aiPredictionService,
        IConfiguration configuration
    )
    {
        _context = context;
        _aiPredictionService = aiPredictionService;
        _configuration = configuration;
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
            player.SteamId = request.SteamId;

            CreateOrUpdateProfileFromAi(player, aiProfile);

            await _context.SaveChangesAsync();

            return BuildSteamImportResponse(player);
        }

        var useFallback = bool.TryParse(
            _configuration["AiService:UseFallbackWhenUnavailable"],
            out var parsedFallback
        )
            ? parsedFallback
            : true;

        if (!useFallback)
        {
            throw new InvalidOperationException(
                "AI service is unavailable or returned an invalid profile."
            );
        }

        var mockProfile = BuildMockSteamProfile(request.SteamId);

        player.SteamId = request.SteamId;

        CreateOrUpdateProfileFromMock(player, mockProfile);

        await _context.SaveChangesAsync();

        return BuildSteamImportResponse(player);
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

    private void CreateOrUpdateProfileFromMock(Player player, MockSteamProfile mockProfile)
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

        player.NormalizedProfile.Source = "steam_mock";

        player.NormalizedProfile.Combat = mockProfile.Combat;
        player.NormalizedProfile.Exploration = mockProfile.Exploration;
        player.NormalizedProfile.Puzzle = mockProfile.Puzzle;

        player.NormalizedProfile.TotalPlaytime = mockProfile.TotalPlaytime;
        player.NormalizedProfile.NumGames = mockProfile.NumGames;

        player.NormalizedProfile.GamesCombat = mockProfile.GamesCombat;
        player.NormalizedProfile.GamesExploration = mockProfile.GamesExploration;
        player.NormalizedProfile.GamesPuzzle = mockProfile.GamesPuzzle;

        player.NormalizedProfile.HoursCombat = mockProfile.HoursCombat;
        player.NormalizedProfile.HoursExploration = mockProfile.HoursExploration;
        player.NormalizedProfile.HoursPuzzle = mockProfile.HoursPuzzle;

        player.NormalizedProfile.AvgPlaytimePerGame =
            mockProfile.NumGames > 0 ? mockProfile.TotalPlaytime / mockProfile.NumGames : 0;

        player.NormalizedProfile.Diversity = CalculateDiversity(
            mockProfile.Combat,
            mockProfile.Exploration,
            mockProfile.Puzzle
        );

        player.NormalizedProfile.Entropy = CalculateEntropy(
            mockProfile.Combat,
            mockProfile.Exploration,
            mockProfile.Puzzle
        );

        player.NormalizedProfile.Dominance = CalculateDominance(
            mockProfile.Combat,
            mockProfile.Exploration,
            mockProfile.Puzzle
        );

        player.NormalizedProfile.SecondMax = CalculateSecondMax(
            mockProfile.Combat,
            mockProfile.Exploration,
            mockProfile.Puzzle
        );

        player.NormalizedProfile.Gap =
            player.NormalizedProfile.Dominance.Value - player.NormalizedProfile.SecondMax.Value;
    }

    private static SteamImportResponse BuildSteamImportResponse(Player player)
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

    private static MockSteamProfile BuildMockSteamProfile(string steamId)
    {
        var seed = Math.Abs(steamId.GetHashCode());
        var profileType = seed % 3;

        return profileType switch
        {
            0 => new MockSteamProfile
            {
                Combat = 0.60,
                Exploration = 0.25,
                Puzzle = 0.15,
                TotalPlaytime = 420,
                NumGames = 18,
                GamesCombat = 10,
                GamesExploration = 5,
                GamesPuzzle = 3,
                HoursCombat = 260,
                HoursExploration = 110,
                HoursPuzzle = 50,
            },

            1 => new MockSteamProfile
            {
                Combat = 0.20,
                Exploration = 0.60,
                Puzzle = 0.20,
                TotalPlaytime = 360,
                NumGames = 16,
                GamesCombat = 4,
                GamesExploration = 9,
                GamesPuzzle = 3,
                HoursCombat = 80,
                HoursExploration = 220,
                HoursPuzzle = 60,
            },

            _ => new MockSteamProfile
            {
                Combat = 0.20,
                Exploration = 0.25,
                Puzzle = 0.55,
                TotalPlaytime = 300,
                NumGames = 14,
                GamesCombat = 3,
                GamesExploration = 4,
                GamesPuzzle = 7,
                HoursCombat = 60,
                HoursExploration = 75,
                HoursPuzzle = 165,
            },
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

    private class MockSteamProfile
    {
        public double Combat { get; set; }

        public double Exploration { get; set; }

        public double Puzzle { get; set; }

        public double TotalPlaytime { get; set; }

        public int NumGames { get; set; }

        public int GamesCombat { get; set; }

        public int GamesExploration { get; set; }

        public int GamesPuzzle { get; set; }

        public double HoursCombat { get; set; }

        public double HoursExploration { get; set; }

        public double HoursPuzzle { get; set; }
    }
}
