using AdaptiveTrials.Application.DTOs.Steam;
using AdaptiveTrials.Application.Interfaces;
using AdaptiveTrials.Domain.Entities;
using AdaptiveTrials.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AdaptiveTrials.Infrastructure.Services;

public class SteamService : ISteamService
{
    private readonly AppDbContext _context;

    public SteamService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<SteamImportResponse?> ImportMockSteamProfileAsync(
        SteamImportRequest request
    )
    {
        var player = await _context.Players
            .Include(player => player.NormalizedProfile)
            .FirstOrDefaultAsync(player => player.Id == request.PlayerId);

        if (player is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.SteamId))
        {
            throw new InvalidOperationException("SteamId is required.");
        }

        var mockProfile = BuildMockSteamProfile(request.SteamId);

        player.SteamId = request.SteamId;

        if (player.NormalizedProfile is null)
        {
            player.NormalizedProfile = new NormalizedProfile
            {
                PlayerId = player.Id,
                Source = "steam_mock",
                CreatedAt = DateTime.UtcNow
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
        player.NormalizedProfile.AvgPlaytimePerGame = mockProfile.TotalPlaytime / mockProfile.NumGames;
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
            player.NormalizedProfile.Dominance.Value -
            player.NormalizedProfile.SecondMax.Value;

        await _context.SaveChangesAsync();

        return new SteamImportResponse
        {
            PlayerId = player.Id,
            SteamId = player.SteamId,
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
            CreatedAt = player.NormalizedProfile.CreatedAt
        };
    }

    private static MockSteamProfile BuildMockSteamProfile(string steamId)
    {
        // Mock determinístico: o mesmo SteamId sempre gera o mesmo perfil.
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
                HoursPuzzle = 50
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
                HoursPuzzle = 60
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
                HoursPuzzle = 165
            }
        };
    }

    private static int CalculateDiversity(params double[] values)
    {
        return values.Count(value => value > 0);
    }

    private static double CalculateEntropy(params double[] values)
    {
        return values
            .Where(value => value > 0)
            .Sum(value => -value * Math.Log(value));
    }

    private static double CalculateDominance(params double[] values)
    {
        return values.Max();
    }

    private static double CalculateSecondMax(params double[] values)
    {
        return values
            .OrderByDescending(value => value)
            .Skip(1)
            .First();
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