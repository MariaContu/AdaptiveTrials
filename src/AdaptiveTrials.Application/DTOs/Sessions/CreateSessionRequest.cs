using AdaptiveTrials.Domain.Enums;

namespace AdaptiveTrials.Application.DTOs.Sessions
{
    public class CreateSessionRequest
    {
        public int? PlayerId { get; set; }
        public GameMode Mode { get; set; }
    }
}