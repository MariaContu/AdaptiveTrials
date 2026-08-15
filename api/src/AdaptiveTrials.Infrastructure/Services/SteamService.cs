using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AdaptiveTrials.Application.DTOs.Steam;
using AdaptiveTrials.Application.Interfaces;
using AdaptiveTrials.Domain.Entities;
using AdaptiveTrials.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AdaptiveTrials.Infrastructure.Services;

public class SteamService : ISteamService
{
    private readonly AppDbContext _context;
    private readonly HttpClient _httpClient;

    public SteamService(
        AppDbContext context,
        HttpClient httpClient
    )
    {
        _context = context;
        _httpClient = httpClient;
    }

    public async Task<SteamImportResponse?> ImportSteamProfileAsync(
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

        var prediction = await PredictSteamProfileAsync(request.SteamId);

        player.SteamId = request.SteamId;

        if (player.NormalizedProfile is null)
        {
            player.NormalizedProfile = new NormalizedProfile
            {
                PlayerId = player.Id
            };

            _context.NormalizedProfiles.Add(player.NormalizedProfile);
        }

        var profile = player.NormalizedProfile;

        profile.Source = "steam_ai_random_forest";

        // The AI model uses the academic macro category
        // `strategic_reasoning`. The game/backend still uses `Puzzle`
        // as the corresponding mission category.
        profile.Combat = prediction.Probabilities.Combat;
        profile.Exploration = prediction.Probabilities.Exploration;
        profile.Puzzle = prediction.Probabilities.StrategicReasoning;

        profile.TotalPlaytime =
            prediction.LibrarySummary.TotalPlaytimeHours;
        profile.NumGames =
            prediction.LibrarySummary.NumGames;

        profile.GamesCombat =
            prediction.LibrarySummary.GamesCombat;
        profile.GamesExploration =
            prediction.LibrarySummary.GamesExploration;
        profile.GamesPuzzle =
            prediction.LibrarySummary.GamesStrategicReasoning;

        profile.HoursCombat =
            prediction.LibrarySummary.HoursCombat;
        profile.HoursExploration =
            prediction.LibrarySummary.HoursExploration;
        profile.HoursPuzzle =
            prediction.LibrarySummary.HoursStrategicReasoning;

        profile.AvgPlaytimePerGame =
            profile.NumGames > 0
                ? profile.TotalPlaytime / profile.NumGames
                : 0;

        profile.Diversity = CalculateDiversity(
            profile.Combat,
            profile.Exploration,
            profile.Puzzle
        );

        profile.Entropy = CalculateEntropy(
            profile.Combat,
            profile.Exploration,
            profile.Puzzle
        );

        profile.Dominance = CalculateDominance(
            profile.Combat,
            profile.Exploration,
            profile.Puzzle
        );

        profile.SecondMax = CalculateSecondMax(
            profile.Combat,
            profile.Exploration,
            profile.Puzzle
        );

        profile.Gap =
            profile.Dominance.Value -
            profile.SecondMax.Value;

        profile.CreatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return new SteamImportResponse
        {
            PlayerId = player.Id,
            SteamId = player.SteamId,
            Source = profile.Source,
            Combat = profile.Combat,
            Exploration = profile.Exploration,
            Puzzle = profile.Puzzle,
            TotalPlaytime = profile.TotalPlaytime,
            NumGames = profile.NumGames,
            GamesCombat = profile.GamesCombat,
            GamesExploration = profile.GamesExploration,
            GamesPuzzle = profile.GamesPuzzle,
            HoursCombat = profile.HoursCombat,
            HoursExploration = profile.HoursExploration,
            HoursPuzzle = profile.HoursPuzzle,
            CreatedAt = profile.CreatedAt
        };
    }

    private async Task<AiPredictionResponse> PredictSteamProfileAsync(
        string steamId
    )
    {
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.PostAsJsonAsync(
                "predict",
                new AiPredictionRequest
                {
                    SteamId = steamId
                }
            );
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                "AI inference service is unavailable.",
                exception
            );
        }
        catch (TaskCanceledException exception)
        {
            throw new InvalidOperationException(
                "AI inference service timed out.",
                exception
            );
        }

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var body = await response.Content.ReadAsStringAsync();

            throw new InvalidOperationException(
                "Steam profile could not be classified because its "
                + $"mapped playtime coverage is insufficient. Details: {body}"
            );
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();

            throw new InvalidOperationException(
                "AI inference failed with HTTP "
                + $"{(int)response.StatusCode}. Details: {body}"
            );
        }

        var prediction =
            await response.Content.ReadFromJsonAsync<AiPredictionResponse>();

        if (prediction is null)
        {
            throw new InvalidOperationException(
                "AI inference service returned an empty response."
            );
        }

        return prediction;
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

    private sealed class AiPredictionRequest
    {
        public string SteamId { get; set; } = string.Empty;
    }

    private sealed class AiPredictionResponse
    {
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("steamId")]
        public string SteamId { get; set; } = string.Empty;

        [JsonPropertyName("predicted_category")]
        public string PredictedCategory { get; set; } = string.Empty;

        public AiProbabilities Probabilities { get; set; } = new();

        [JsonPropertyName("profile_quality")]
        public AiProfileQuality ProfileQuality { get; set; } = new();

        [JsonPropertyName("library_summary")]
        public AiLibrarySummary LibrarySummary { get; set; } = new();

        [JsonPropertyName("feature_count")]
        public int FeatureCount { get; set; }

        [JsonPropertyName("feature_set")]
        public string FeatureSet { get; set; } = string.Empty;
    }

    private sealed class AiProbabilities
    {
        public double Combat { get; set; }

        public double Exploration { get; set; }

        [JsonPropertyName("strategic_reasoning")]
        public double StrategicReasoning { get; set; }
    }

    private sealed class AiProfileQuality
    {
        [JsonPropertyName("category_game_coverage")]
        public double CategoryGameCoverage { get; set; }

        [JsonPropertyName("category_playtime_coverage")]
        public double CategoryPlaytimeCoverage { get; set; }
    }

    private sealed class AiLibrarySummary
    {
        [JsonPropertyName("total_playtime_hours")]
        public double TotalPlaytimeHours { get; set; }

        [JsonPropertyName("num_games")]
        public int NumGames { get; set; }

        [JsonPropertyName("games_combat")]
        public int GamesCombat { get; set; }

        [JsonPropertyName("games_exploration")]
        public int GamesExploration { get; set; }

        [JsonPropertyName("games_strategic_reasoning")]
        public int GamesStrategicReasoning { get; set; }

        [JsonPropertyName("hours_combat")]
        public double HoursCombat { get; set; }

        [JsonPropertyName("hours_exploration")]
        public double HoursExploration { get; set; }

        [JsonPropertyName("hours_strategic_reasoning")]
        public double HoursStrategicReasoning { get; set; }
    }
}