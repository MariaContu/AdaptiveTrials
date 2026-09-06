using System.Net.Http.Json;
using AdaptiveTrials.Application.DTOs.Ai;
using AdaptiveTrials.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AdaptiveTrials.Infrastructure.Services;

public class AiPredictionService : IAiPredictionService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiPredictionService> _logger;

    public AiPredictionService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<AiPredictionService> logger
    )
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AiPredictionResponse?> PredictFromSteamIdAsync(string steamId)
    {
        try
        {
            var baseUrl = _configuration["AiService:BaseUrl"];

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new InvalidOperationException("AI service BaseUrl is not configured.");
            }

            _httpClient.BaseAddress = new Uri(baseUrl);

            var request = new AiPredictionRequest { SteamId = steamId };

            var response = await _httpClient.PostAsJsonAsync("/predict", request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "AI service returned status code {StatusCode}",
                    response.StatusCode
                );

                return null;
            }

            return await response.Content.ReadFromJsonAsync<AiPredictionResponse>();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not get prediction from AI service.");

            return null;
        }
    }
}
