namespace AdaptiveTrials.Application.DTOs.Steam;

public class SteamImportRequest
{
    public int PlayerId { get; set; }

    public string SteamId { get; set; } = string.Empty;
}