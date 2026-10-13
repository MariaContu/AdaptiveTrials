namespace AdaptiveTrials.Application.Exceptions;

/// <summary>
/// Requisicao valida, mas fora da sequencia de uma sessao adaptativa.
/// </summary>
public sealed class AdaptiveSessionConflictException : InvalidOperationException
{
    public AdaptiveSessionConflictException(string message) : base(message) { }
}
